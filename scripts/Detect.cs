// Megabat test: open-vocabulary object detection in a point cloud with Grounding DINO
// Renders views from the centre of the cloud (indoor), runs C:\megabat_detect\detect.py, maps the 2D boxes back to the points.
// Inputs:  C (PointCloud), prompt (text, e.g. "radiator. toilet. person."), cam (points and/or names of Rhino views, "active" = current view; empty = centre), views (int per point, default 8),
//          score (double, default 0.35), depth (double, max depth of an object behind its front [m], default 0.5), run (bool)
// Outputs: Clouds (one cloud per object), Label, Score, Box, Info
#region Usings
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Rhino;
using Rhino.Geometry;
using Grasshopper;
using Grasshopper.Kernel;
#endregion

public class Script_Instance : GH_ScriptInstance
{
  const string Py = @"C:\megabat_detect\venv\Scripts\python.exe";
  const string Script = @"C:\megabat_detect\detect.py";
  const string Dir = @"C:\megabat_detect\views";
  const int W = 1024, H = 768;

  List<PointCloud> _clouds = new List<PointCloud>();
  List<string> _labels = new List<string>();
  List<double> _scores = new List<double>();
  List<Box> _boxes = new List<Box>();
  string _info = "set run = true";

  class View { public Point3d Cam; public Vector3d F, R, U; public double Fx; public int[] Idx; public float[] Z; }
  class Hit { public string Label; public double Score; public HashSet<int> Pts; public BoundingBox BB; }

  private void RunScript(
		object C,
		object prompt,
		object views,
		object score,
		object depth,
		object run,
		ref object Clouds,
		ref object Label,
		ref object Score,
		ref object Box,
		ref object Info)
  {
    Component.Message = "Detect";
    var pc = C as PointCloud;
    bool go = run != null && Convert.ToBoolean(run);
    if (go && pc != null && pc.Count > 0 && prompt != null) Detect(pc, prompt.ToString(), cam,
      views == null ? 8 : Convert.ToInt32(views), score == null ? 0.35 : Convert.ToDouble(score), depth == null ? 0.5 : Convert.ToDouble(depth));
    Clouds = _clouds; Label = _labels; Score = _scores; Box = _boxes; Info = _info;
  }

  void Detect(PointCloud pc, string prompt, object camIn, int n, double score, double depth)
  {
    var sw = System.Diagnostics.Stopwatch.StartNew();
    Directory.CreateDirectory(Dir);
    var pts = pc.GetPoints();
    var cols = pc.ContainsColors ? pc.GetColors() : null;
    var bb = new BoundingBox(pts);
    // 1 views: points = n horizontal views from each point; text = named Rhino view or "active"; empty = centre of the cloud
    var items = new List<object>();
    if (camIn is System.Collections.IEnumerable en && !(camIn is string)) { foreach (var o in en) if (o != null) items.Add(o); }
    else if (camIn != null) items.Add(camIn);
    if (items.Count == 0) items.Add(new Point3d(bb.Center.X, bb.Center.Y, bb.Min.Z + Math.Min(1.6, 0.6 * (bb.Max.Z - bb.Min.Z))));
    var vs = new List<View>(); var imgs = new List<string>(); var notes = new List<string>();
    var rdoc = RhinoDoc.ActiveDoc;
    foreach (var it in items)
    {
      object val = it is Grasshopper.Kernel.Types.IGH_Goo g ? g.ScriptVariable() : it;
      if (val is Point3d p0)
      {
        for (int k = 0; k < Math.Max(1, n); k++)
        {
          double ang = 2 * Math.PI * k / Math.Max(1, n);
          var v = new View { Cam = p0, F = new Vector3d(Math.Cos(ang), Math.Sin(ang), 0), U = Vector3d.ZAxis };
          v.R = Vector3d.CrossProduct(v.F, v.U); v.Fx = (W / 2.0) / Math.Tan(Math.PI / 4);
          vs.Add(v);
        }
      }
      else if (val is string s && rdoc != null)
      {
        Rhino.DocObjects.ViewportInfo vi = null;
        if (s.Trim().ToLower() == "active") vi = new Rhino.DocObjects.ViewportInfo(rdoc.Views.ActiveView.ActiveViewport);
        else { int idx = rdoc.NamedViews.FindByName(s.Trim()); if (idx >= 0) vi = rdoc.NamedViews[idx].Viewport; }
        if (vi == null) { notes.Add("view not found: " + s); continue; }
        if (!vi.IsPerspectiveProjection) { notes.Add("not perspective: " + s); continue; }
        vi.GetCameraAngles(out double hd, out double hv, out double hh);
        var f = vi.CameraDirection; f.Unitize();
        var r = Vector3d.CrossProduct(f, vi.CameraUp); r.Unitize();
        var u = Vector3d.CrossProduct(r, f); u.Unitize();
        vs.Add(new View { Cam = vi.CameraLocation, F = f, R = r, U = u, Fx = (W / 2.0) / Math.Tan(hh) });
      }
      else notes.Add("ignored input: " + val);
    }
    for (int k = 0; k < vs.Count; k++)
    {
      var path = Path.Combine(Dir, "view_" + k + ".bmp");
      Render(vs[k], pts, cols, path); imgs.Add(path);
    }
    long tRender = sw.ElapsedMilliseconds;

    // 2 Grounding DINO
    var job = Path.Combine(Dir, "job.json"); var res = Path.Combine(Dir, "result.json");
    File.WriteAllText(job, System.Text.Json.JsonSerializer.Serialize(new { images = imgs, prompt = prompt, score = score }));
    if (File.Exists(res)) File.Delete(res);
    var psi = new System.Diagnostics.ProcessStartInfo(Py, "\"" + Script + "\" \"" + job + "\" \"" + res + "\"")
    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
    var p = System.Diagnostics.Process.Start(psi);
    var err = p.StandardError.ReadToEnd(); p.StandardOutput.ReadToEnd(); p.WaitForExit(600000);
    if (!File.Exists(res)) { _info = "detect.py failed:\n" + (err.Length > 1500 ? err.Substring(err.Length - 1500) : err); return; }
    var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(res)).RootElement;
    long tDetect = sw.ElapsedMilliseconds;

    // 3 boxes back to points: front-most point per pixel, then keep the near part (depth)
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
        var keep = new HashSet<int>(ids.Where(i => (pts[i] - v.Cam) * v.F <= z0 + depth));
        if (keep.Count < 10) continue;
        hits.Add(new Hit { Label = d.GetProperty("label").GetString(), Score = d.GetProperty("score").GetDouble(), Pts = keep, BB = new BoundingBox(keep.Select(i => pts[i])) });
        c++;
      }
      perView.Add(c);
    }

    // 4 merge the same object seen in several views (same label, overlapping boxes)
    var merged = new List<Hit>();
    foreach (var h in hits.OrderByDescending(x => x.Score))
    {
      var m = merged.FirstOrDefault(x => x.Label == h.Label && Overlap(x.BB, h.BB) > 0.3);
      if (m == null) merged.Add(h);
      else { m.Pts.UnionWith(h.Pts); m.BB.Union(h.BB); m.Score = Math.Max(m.Score, h.Score); }
    }

    _clouds = new List<PointCloud>(); _labels = new List<string>(); _scores = new List<double>(); _boxes = new List<Box>();
    foreach (var m in merged)
    {
      var o = new PointCloud();
      foreach (var i in m.Pts) { if (cols != null) o.Add(pts[i], cols[i]); else o.Add(pts[i]); }
      _clouds.Add(o); _labels.Add(m.Label); _scores.Add(Math.Round(m.Score, 3)); _boxes.Add(new Box(m.BB));
    }
    _info = string.Format("{0} objects ({1} detections in {2} views: {3}); {4}; render {5} ms, DINO {6:0.0} s (model load {7:0.0} s, {8}), total {9:0.0} s; views in {10}{11}",
      merged.Count, hits.Count, vs.Count, string.Join(" ", perView),
      string.Join(", ", merged.GroupBy(x => x.Label).Select(g => g.Key + " " + g.Count())),
      tRender, (tDetect - tRender) / 1000.0, doc.GetProperty("load_s").GetDouble(), doc.GetProperty("device").GetString(), sw.ElapsedMilliseconds / 1000.0, Dir, notes.Count > 0 ? "; " + string.Join("; ", notes) : "");
  }

  // Share of the smaller box that lies inside the other
  static double Overlap(BoundingBox a, BoundingBox b)
  {
    var i = BoundingBox.Intersection(a, b);
    if (!i.IsValid) return 0;
    double vi = i.Volume, va = a.Volume, vb = b.Volume;
    double m = Math.Min(va, vb);
    return m <= 0 ? 0 : vi / m;
  }

  // Splat rendering with z-buffer; splat size grows when points are close to the camera
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
    // 24-bit BMP written by hand (no System.Drawing.Bitmap needed)
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
