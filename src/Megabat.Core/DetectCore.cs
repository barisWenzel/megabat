// Open-vocabulary object detection in a point cloud with Grounding DINO (experimental):
// 1 render views (splats with z-buffer), 2 run detect.py in a separate Python process, 3 map the 2D boxes back to the
// front-most points, 4 merge detections of the same object over views (shared 10 cm cells or overlapping boxes),
// 5 keep objects confirmed in enough views
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace Megabat.Core
{
  public static class DetectCore
  {
    public const int W = 1024, H = 768;

    public class Options
    {
      public string Folder = @"C:\megabat_detect";   // holds venv\Scripts\python.exe and detect.py; views are written to Folder\views
      public int Views = 8;                           // horizontal views per standpoint
      public double Score = 0.45, Depth = 0.5;
      public int MinViews = 2, MinPoints = 300;
      public int TimeoutS = 600;
    }

    public class Obj { public string Label; public double Score; public PointCloud Cloud; public Box Box; public int Views; }
    public class Result { public List<Obj> Objects = new List<Obj>(); public string Info = ""; public string Error; }

    class View { public Point3d Cam; public Vector3d F, R, U; public double Fx; public int[] Idx; public float[] Z; }
    class Hit { public string Label; public double Score; public HashSet<int> Pts; public BoundingBox BB; public int View; }

    // standpoints: n horizontal views each; cams: perspective viewports (e.g. named views); both empty = centre of the cloud
    public static Result Run(PointCloud pc, string prompt, IList<Point3d> standpoints, IList<ViewportInfo> cams, Options o)
    {
      var res = new Result();
      var sw = System.Diagnostics.Stopwatch.StartNew();
      string py = Path.Combine(o.Folder, "venv", "Scripts", "python.exe"), script = Path.Combine(o.Folder, "detect.py"), dir = Path.Combine(o.Folder, "views");
      if (!File.Exists(py)) { res.Error = "Python not found: " + py + " (see the setup in the Detect docs)"; return res; }
      if (!File.Exists(script)) { res.Error = "detect.py not found: " + script; return res; }
      Directory.CreateDirectory(dir);

      var pts = pc.GetPoints();
      var cols = pc.ContainsColors ? pc.GetColors() : null;
      var bb = new BoundingBox(pts);

      // 1 views
      var vs = new List<View>();
      var sp = standpoints?.ToList() ?? new List<Point3d>();
      if (sp.Count == 0 && (cams == null || cams.Count == 0))
        sp.Add(new Point3d(bb.Center.X, bb.Center.Y, bb.Min.Z + Math.Min(1.6, 0.6 * (bb.Max.Z - bb.Min.Z))));
      int n = Math.Max(1, o.Views);
      foreach (var p0 in sp)
        for (int k = 0; k < n; k++)
        {
          double a = 2 * Math.PI * k / n;
          var v = new View { Cam = p0, F = new Vector3d(Math.Cos(a), Math.Sin(a), 0), U = Vector3d.ZAxis };
          v.R = Vector3d.CrossProduct(v.F, v.U); v.Fx = (W / 2.0) / Math.Tan(Math.PI / 4);
          vs.Add(v);
        }
      if (cams != null)
        foreach (var vi in cams)
        {
          if (vi == null || !vi.IsPerspectiveProjection) continue;
          vi.GetCameraAngles(out _, out _, out double hh);
          var f = vi.CameraDirection; f.Unitize();
          var r = Vector3d.CrossProduct(f, vi.CameraUp); r.Unitize();
          var u = Vector3d.CrossProduct(r, f); u.Unitize();
          vs.Add(new View { Cam = vi.CameraLocation, F = f, R = r, U = u, Fx = (W / 2.0) / Math.Tan(hh) });
        }
      var imgs = new List<string>();
      for (int k = 0; k < vs.Count; k++) { var path = Path.Combine(dir, "view_" + k + ".bmp"); Render(vs[k], pts, cols, path); imgs.Add(path); }
      long tRender = sw.ElapsedMilliseconds;

      // 2 Grounding DINO in a separate process
      var job = Path.Combine(dir, "job.json"); var resf = Path.Combine(dir, "result.json");
      File.WriteAllText(job, System.Text.Json.JsonSerializer.Serialize(new { images = imgs, prompt = prompt, score = o.Score }));
      if (File.Exists(resf)) File.Delete(resf);
      var psi = new System.Diagnostics.ProcessStartInfo(py, "\"" + script + "\" \"" + job + "\" \"" + resf + "\"")
      { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
      using (var p = System.Diagnostics.Process.Start(psi))
      {
        var errTask = p.StandardError.ReadToEndAsync(); var outTask = p.StandardOutput.ReadToEndAsync();
        if (!p.WaitForExit(o.TimeoutS * 1000)) { try { p.Kill(); } catch { } res.Error = "detect.py timed out"; return res; }
        var err = errTask.Result;
        if (!File.Exists(resf)) { res.Error = "detect.py failed:\n" + (err.Length > 1500 ? err.Substring(err.Length - 1500) : err); return res; }
      }
      var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(resf)).RootElement;
      long tDetect = sw.ElapsedMilliseconds;

      // 3 boxes back to points: front-most point per pixel, then the near part (depth)
      var hits = new List<Hit>(); var perView = new List<int>();
      var vlist = doc.GetProperty("views");
      for (int k = 0; k < vs.Count; k++)
      {
        var v = vs[k]; int c = 0;
        foreach (var d in vlist[k].EnumerateArray())
        {
          var b = d.GetProperty("box").EnumerateArray().Select(x => x.GetDouble()).ToArray();
          int x0 = Math.Max(0, (int)b[0]), y0 = Math.Max(0, (int)b[1]), x1 = Math.Min(W - 1, (int)b[2]), y1 = Math.Min(H - 1, (int)b[3]);
          var ids = new HashSet<int>(); var zs = new List<float>();
          for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
              int i = v.Idx[y * W + x];
              if (i >= 0 && ids.Add(i)) zs.Add(v.Z[y * W + x]);
            }
          if (ids.Count < 10) continue;
          zs.Sort(); double z0 = zs[zs.Count / 5];
          var keep = new HashSet<int>(ids.Where(i => (pts[i] - v.Cam) * v.F <= z0 + o.Depth));
          if (keep.Count < 10) continue;
          hits.Add(new Hit { Label = d.GetProperty("label").GetString(), Score = d.GetProperty("score").GetDouble(), Pts = keep, BB = new BoundingBox(keep.Select(i => pts[i])), View = k });
          c++;
        }
        perView.Add(c);
      }

      // 4 merge over views: same label and shared 10 cm cells or overlapping boxes, transitive (union-find)
      var cells = hits.Select(h => new HashSet<long>(h.Pts.Select(i => Cell(pts[i], 0.1)))).ToList();
      var par = Enumerable.Range(0, hits.Count).ToArray();
      int Find(int x) { while (par[x] != x) x = par[x] = par[par[x]]; return x; }
      for (int a = 0; a < hits.Count; a++)
        for (int b = a + 1; b < hits.Count; b++)
        {
          if (hits[a].Label != hits[b].Label || Find(a) == Find(b)) continue;
          var sa = cells[a].Count < cells[b].Count ? cells[a] : cells[b]; var sb = ReferenceEquals(sa, cells[a]) ? cells[b] : cells[a];
          double share = (double)sa.Count(c => sb.Contains(c)) / Math.Max(1, sa.Count);
          if (share > 0.2 || Overlap(hits[a].BB, hits[b].BB) > 0.3) par[Find(a)] = Find(b);
        }

      // 5 confirmation in enough views and with enough points
      int need = Math.Max(1, Math.Min(o.MinViews, vs.Count)), dropped = 0;
      foreach (var g in Enumerable.Range(0, hits.Count).GroupBy(Find))
      {
        var ids = new HashSet<int>(); var box = BoundingBox.Empty;
        foreach (var i in g) { ids.UnionWith(hits[i].Pts); box.Union(hits[i].BB); }
        int views = g.Select(i => hits[i].View).Distinct().Count();
        if (views < need || ids.Count < o.MinPoints) { dropped++; continue; }
        var best = g.OrderByDescending(i => hits[i].Score).First();
        var cl = new PointCloud();
        foreach (var i in ids) { if (cols != null) cl.Add(pts[i], cols[i]); else cl.Add(pts[i]); }
        res.Objects.Add(new Obj { Label = hits[best].Label, Score = Math.Round(hits[best].Score, 3), Cloud = cl, Box = new Box(box), Views = views });
      }

      res.Info = string.Format("{0} objects ({1}); {2} detections in {3} views ({4}); {5} dropped (fewer than {6} views or {7} points); render {8:0.0} s, DINO {9:0.0} s (model load {10:0.0} s, {11}); views in {12}",
        res.Objects.Count, string.Join(", ", res.Objects.GroupBy(x => x.Label).Select(g => g.Key + " " + g.Count())),
        hits.Count, vs.Count, string.Join(" ", perView), dropped, need, o.MinPoints,
        tRender / 1000.0, (tDetect - tRender) / 1000.0, doc.GetProperty("load_s").GetDouble(), doc.GetProperty("device").GetString(), dir);
      return res;
    }

    static long Cell(Point3d p, double v) => ((long)Math.Floor(p.X / v) * 1000003L + (long)Math.Floor(p.Y / v)) * 1000033L + (long)Math.Floor(p.Z / v);

    // share of the smaller box inside the other
    static double Overlap(BoundingBox a, BoundingBox b)
    {
      var i = BoundingBox.Intersection(a, b);
      if (!i.IsValid) return 0;
      double m = Math.Min(a.Volume, b.Volume);
      return m <= 0 ? 0 : i.Volume / m;
    }

    // splats with z-buffer (larger when close), written as 24-bit BMP
    static void Render(View v, Point3d[] pts, Color[] cols, string path)
    {
      v.Idx = Enumerable.Repeat(-1, W * H).ToArray(); v.Z = Enumerable.Repeat(float.MaxValue, W * H).ToArray();
      for (int i = 0; i < pts.Length; i++)
      {
        var d = pts[i] - v.Cam;
        double z = d * v.F; if (z < 0.1) continue;
        double u = W / 2.0 + (d * v.R) / z * v.Fx, w = H / 2.0 - (d * v.U) / z * v.Fx;
        int r = Math.Max(1, Math.Min(5, (int)(v.Fx * 0.015 / z)));
        int cx = (int)u, cy = (int)w;
        if (cx < -r || cy < -r || cx >= W + r || cy >= H + r) continue;
        for (int y = Math.Max(0, cy - r); y <= Math.Min(H - 1, cy + r); y++)
          for (int x = Math.Max(0, cx - r); x <= Math.Min(W - 1, cx + r); x++)
          {
            int q = y * W + x;
            if (z < v.Z[q]) { v.Z[q] = (float)z; v.Idx[q] = i; }
          }
      }
      int stride = (3 * W + 3) / 4 * 4;
      var buf = new byte[54 + stride * H];
      void I32(int o, int val) { BitConverter.GetBytes(val).CopyTo(buf, o); }
      buf[0] = (byte)'B'; buf[1] = (byte)'M'; I32(2, buf.Length); I32(10, 54); I32(14, 40); I32(18, W); I32(22, H);
      buf[26] = 1; buf[28] = 24; I32(34, stride * H);
      for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
          int i = v.Idx[y * W + x], o = 54 + (H - 1 - y) * stride + 3 * x;
          Color c = i < 0 ? Color.FromArgb(128, 128, 128) : cols != null ? cols[i] : Shade(v.Z[y * W + x]);
          buf[o] = c.B; buf[o + 1] = c.G; buf[o + 2] = c.R;
        }
      File.WriteAllBytes(path, buf);
    }
    static Color Shade(float z) { int g = Math.Max(40, Math.Min(230, (int)(230 - z * 25))); return Color.FromArgb(g, g, g); }
  }
}
