// Megabat test: rectangle from SegmentRect intersected with the raster outline of the segment points
// Inputs:  R (Curve) rectangle from SegmentRect, P (PointCloud) points of the segment,
//          cell (double) raster size [m], close (int) cells for closing gaps, edge (double) max gap to the rectangle edge [m]
// Outputs: Outline (polylines), Srf (planar breps, openings as holes), Info (text)
#region Usings
using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.Geometry;
using Grasshopper;
using Grasshopper.Kernel;
#endregion

public class Script_Instance : GH_ScriptInstance
{
  private void RunScript(object R, object P, object cell, object close, object edge, ref object Outline, ref object Srf, ref object Info)
  {
    Component.Message = "RectOutline";
    Curve rect = null; GH_Convert.ToCurve(R, ref rect, GH_Conversion.Both);
    var pc = P as PointCloud;
    double cs = cell == null ? 0.05 : Convert.ToDouble(cell);
    int cl = close == null ? 2 : Convert.ToInt32(close);
    double eg = edge == null ? 0.3 : Convert.ToDouble(edge);
    if (rect == null || pc == null || pc.Count == 0) return;
    if (!rect.TryGetPolyline(out Polyline pl) || pl.Count < 5) { Component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "R must be a closed rectangle"); return; }
    if (cs <= 0) cs = 0.05;
    var sw = System.Diagnostics.Stopwatch.StartNew();

    // Frame of the rectangle: origin corner 0, u along edge 0-1, v along edge 0-3
    var o = pl[0]; var U = pl[1] - pl[0]; var V = pl[3] - pl[0];
    double W = U.Length, H = V.Length; U.Unitize(); V.Unitize();
    int nx = Math.Max(1, (int)Math.Round(W / cs)), ny = Math.Max(1, (int)Math.Round(H / cs));
    double cw = W / nx, ch = H / ny;   // cells fit the rectangle exactly

    // 1 occupancy
    var g = new bool[nx, ny];
    foreach (var it in pc)
    {
      var d = it.Location - o;
      int i = (int)Math.Floor(d * U / cw), j = (int)Math.Floor(d * V / ch);
      if (i >= 0 && j >= 0 && i < nx && j < ny) g[i, j] = true;
    }
    int raw = Count(g);

    // 2 closing: dilate then erode
    if (cl > 0) { g = Morph(g, cl, true); g = Morph(g, cl, false); }

    // 3 reach the (snapped) rectangle edges: fill the gap between edge and first occupied cell if short
    int ex = (int)Math.Round(eg / cw), ey = (int)Math.Round(eg / ch);
    FillEdges(g, ex, ey);

    // 4 boundary loops along the cell edges, collinear points merged
    var loops = Loops(g);
    var outl = new List<Polyline>();
    foreach (var lp in loops)
    {
      var p = new Polyline(lp.Select(q => o + q.X * cw * U + q.Y * ch * V));
      p.Add(p[0]);
      outl.Add(p);
    }
    var crv = outl.Select(p => (Curve)new PolylineCurve(p)).ToList();
    var breps = crv.Count > 0 ? Brep.CreatePlanarBreps(crv, RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.001) : null;

    double area = breps == null ? 0 : breps.Sum(b => b.GetArea());
    Outline = outl;
    Srf = breps;
    Info = string.Format("{0} × {1} cells of {2:0.000} × {3:0.000} m, occupied {4:0} % raw, {5:0} % after closing, {6} loops, area {7:0.00} of {8:0.00} m² ({9:0} %), {10} ms",
      nx, ny, cw, ch, 100.0 * raw / (nx * ny), 100.0 * Count(g) / (nx * ny), outl.Count, area, W * H, 100 * area / (W * H), sw.ElapsedMilliseconds);
  }

  static int Count(bool[,] g) { int c = 0; foreach (var b in g) if (b) c++; return c; }

  // Square structuring element (2r+1); separable: rows then columns
  static bool[,] Morph(bool[,] g, int r, bool dilate)
  {
    int nx = g.GetLength(0), ny = g.GetLength(1);
    var a = new bool[nx, ny]; var b = new bool[nx, ny];
    for (int j = 0; j < ny; j++)
      for (int i = 0; i < nx; i++)
      {
        bool v = !dilate;
        for (int k = Math.Max(0, i - r); k <= Math.Min(nx - 1, i + r); k++)
          if (dilate ? g[k, j] : !g[k, j]) { v = dilate; break; }
        a[i, j] = v;
      }
    for (int i = 0; i < nx; i++)
      for (int j = 0; j < ny; j++)
      {
        bool v = !dilate;
        for (int k = Math.Max(0, j - r); k <= Math.Min(ny - 1, j + r); k++)
          if (dilate ? a[i, k] : !a[i, k]) { v = dilate; break; }
        b[i, j] = v;
      }
    return b;
  }

  static void FillEdges(bool[,] g, int ex, int ey)
  {
    int nx = g.GetLength(0), ny = g.GetLength(1);
    for (int j = 0; j < ny; j++)
    {
      int a = 0; while (a < nx && !g[a, j]) a++;
      if (a < nx && a <= ex) for (int i = 0; i < a; i++) g[i, j] = true;
      int b = nx - 1; while (b >= 0 && !g[b, j]) b--;
      if (b >= 0 && nx - 1 - b <= ex) for (int i = b + 1; i < nx; i++) g[i, j] = true;
    }
    for (int i = 0; i < nx; i++)
    {
      int a = 0; while (a < ny && !g[i, a]) a++;
      if (a < ny && a <= ey) for (int j = 0; j < a; j++) g[i, j] = true;
      int b = ny - 1; while (b >= 0 && !g[i, b]) b--;
      if (b >= 0 && ny - 1 - b <= ey) for (int j = b + 1; j < ny; j++) g[i, j] = true;
    }
  }

  // Directed cell edges with the occupied cell on the left, chained into loops
  static List<List<Point2d>> Loops(bool[,] g)
  {
    int nx = g.GetLength(0), ny = g.GetLength(1);
    bool Occ(int i, int j) => i >= 0 && j >= 0 && i < nx && j < ny && g[i, j];
    long K(int x, int y) => (long)x * 1000003L + y;
    var next = new Dictionary<long, List<(int x, int y)>>();
    void Add(int x0, int y0, int x1, int y1)
    {
      long k = K(x0, y0);
      if (!next.TryGetValue(k, out var l)) next[k] = l = new List<(int, int)>();
      l.Add((x1, y1));
    }
    for (int i = 0; i < nx; i++)
      for (int j = 0; j < ny; j++)
      {
        if (!g[i, j]) continue;
        if (!Occ(i, j - 1)) Add(i, j, i + 1, j);
        if (!Occ(i + 1, j)) Add(i + 1, j, i + 1, j + 1);
        if (!Occ(i, j + 1)) Add(i + 1, j + 1, i, j + 1);
        if (!Occ(i - 1, j)) Add(i, j + 1, i, j);
      }
    var res = new List<List<Point2d>>();
    while (next.Count > 0)
    {
      var startKey = next.Keys.First();
      int sx = (int)(startKey / 1000003L), sy = (int)(startKey % 1000003L);
      var pts = new List<(int x, int y)> { (sx, sy) };
      int cx = sx, cy = sy;
      for (int guard = 0; guard < 4 * (nx + 1) * (ny + 1); guard++)
      {
        long k = K(cx, cy);
        if (!next.TryGetValue(k, out var l)) break;
        var n = l[l.Count - 1]; l.RemoveAt(l.Count - 1);
        if (l.Count == 0) next.Remove(k);
        cx = n.x; cy = n.y;
        if (cx == sx && cy == sy) break;
        pts.Add((cx, cy));
      }
      // drop collinear vertices
      var s = new List<Point2d>();
      for (int a = 0; a < pts.Count; a++)
      {
        var p0 = pts[(a - 1 + pts.Count) % pts.Count]; var p1 = pts[a]; var p2 = pts[(a + 1) % pts.Count];
        bool col = (p0.x == p1.x && p1.x == p2.x) || (p0.y == p1.y && p1.y == p2.y);
        if (!col) s.Add(new Point2d(p1.x, p1.y));
      }
      if (s.Count >= 3) res.Add(s);
    }
    return res;
  }
}
