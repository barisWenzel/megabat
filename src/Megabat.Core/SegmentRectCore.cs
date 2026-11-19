// Megabat: computation core without Grasshopper dependency
using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace Megabat.Core
{
  // One rectangle per segment, oriented architecturally, edges trimmed to the neighbouring planes
  public static class SegmentRectCore
  {
    public class Rect
    {
      public Plane Frame;            // origin and axes u (XAxis), v (YAxis) in the segment plane
      public double S0, S1, T0, T1;  // extent along u and v
      public bool[] Trimmed = new bool[4];   // edges s0, s1, t0, t1 moved to a neighbour plane
      public int[] Neighbour = { -1, -1, -1, -1 };
      public double Coverage;        // share of the rectangle covered by points (raster)
      public string Orientation;
      public Point3d[] Corners()
      {
        return new[] { P(S0, T0), P(S1, T0), P(S1, T1), P(S0, T1) };
      }
      public Point3d P(double s, double t) => Frame.Origin + s * Frame.XAxis + t * Frame.YAxis;
      public double Width => S1 - S0;
      public double Height => T1 - T0;
    }

    // q: quantile cut per side (0.01 = ignore 1 % outliers); gap: max distance edge to neighbour plane;
    // angTol in degrees; sample: points used per segment
    public static List<Rect> Run(IList<Segment> segs, Vector3d dir, double q, bool trim, double gap, double angTol, int sample)
    {
      if (!dir.IsValid || dir.IsZero) dir = WallDirection(segs);
      var res = new Rect[segs.Count];
      var samples = new List<Point3d>[segs.Count];
      System.Threading.Tasks.Parallel.For(0, segs.Count, i =>
      {
        samples[i] = Sample(segs[i].Points, sample);
        res[i] = Fit(segs[i], samples[i], dir, q);
      });
      if (trim && segs.Count > 1)
      {
        var orig = res.Select(r => new[] { r.S0, r.S1, r.T0, r.T1 }).ToArray();
        double cosTol = Math.Cos(angTol * Math.PI / 180.0);
        for (int i = 0; i < res.Length; i++) TrimEdges(i, res, orig, segs, gap, cosTol);
      }
      System.Threading.Tasks.Parallel.For(0, res.Length, i => res[i].Coverage = Coverage(res[i], samples[i]));
      return res.ToList();
    }

    // Main direction of the walls (modulo 90°, weighted by point count); Unset if there are no walls
    public static Vector3d WallDirection(IList<Segment> segs)
    {
      double c = 0, sn = 0;
      foreach (var g in segs)
      {
        var n = g.Plane.Normal; n.Unitize();
        if (Math.Abs(n.Z) > 0.1 || g.Points == null) continue;
        double a = Math.Atan2(n.Y, n.X) * 4;   // 4 × angle: directions 90° apart coincide
        double w = g.Points.Count;
        c += w * Math.Cos(a); sn += w * Math.Sin(a);
      }
      if (c == 0 && sn == 0) return Vector3d.Unset;
      double ang = Math.Atan2(sn, c) / 4;
      return new Vector3d(-Math.Sin(ang), Math.Cos(ang), 0);   // along the wall, not the normal
    }

    static List<Point3d> Sample(PointCloud pc, int n)
    {
      var r = new List<Point3d>();
      if (pc == null || pc.Count == 0) return r;
      int step = Math.Max(1, pc.Count / Math.Max(1, n));
      for (int i = 0; i < pc.Count; i += step) r.Add(pc[i].Location);
      return r;
    }

    // Axes: walls horizontal/vertical, inclined faces along the horizontal line (eaves),
    // horizontal faces along the main direction or the minimum-area rectangle
    static Rect Fit(Segment seg, List<Point3d> pts, Vector3d dir, double q)
    {
      var pl = seg.Plane;
      var n = pl.Normal; n.Unitize();
      var z = Vector3d.ZAxis;
      Vector3d u; string how;
      double nz = Math.Abs(n * z);
      if (nz < 0.9999)
      {
        if (nz > 0.9 && TryDir(dir, n, out u)) how = "wall direction";
        else if (nz > 0.9) { u = MinAreaDir(pl, n, pts); how = "minimum rectangle"; }
        else { u = Vector3d.CrossProduct(z, n); u.Unitize(); how = nz < 0.1 ? "wall (horizontal/vertical)" : "inclined (along horizontal line)"; }
      }
      else if (TryDir(dir, n, out u)) how = "wall direction";
      else { u = MinAreaDir(pl, n, pts); how = "minimum rectangle"; }
      var v = Vector3d.CrossProduct(n, u); v.Unitize();
      if (v * z < -1e-9) { v = -v; u = -u; }   // v points upwards where possible
      var frame = new Plane(pl.Origin, u, v);

      var s = new double[pts.Count]; var t = new double[pts.Count];
      for (int i = 0; i < pts.Count; i++) { var d = pts[i] - pl.Origin; s[i] = d * u; t[i] = d * v; }
      Array.Sort(s); Array.Sort(t);
      var r = new Rect { Frame = frame, Orientation = how };
      if (pts.Count == 0) return r;
      r.S0 = Q(s, q); r.S1 = Q(s, 1 - q); r.T0 = Q(t, q); r.T1 = Q(t, 1 - q);
      return r;
    }

    static bool TryDir(Vector3d dir, Vector3d n, out Vector3d u)
    {
      u = dir - (dir * n) * n;
      if (!dir.IsValid || dir.IsZero || u.Length < 0.3 * dir.Length) return false;
      u.Unitize(); return true;
    }

    static double Q(double[] sorted, double q)
    {
      if (sorted.Length == 0) return 0;
      double x = Math.Max(0, Math.Min(1, q)) * (sorted.Length - 1);
      int a = (int)Math.Floor(x), b = Math.Min(sorted.Length - 1, a + 1);
      return sorted[a] + (sorted[b] - sorted[a]) * (x - a);
    }

    // Rotating calipers over the convex hull of the points in the plane
    static Vector3d MinAreaDir(Plane pl, Vector3d n, List<Point3d> pts)
    {
      var a = pl.XAxis; var b = Vector3d.CrossProduct(n, a);
      var p2 = pts.Select(p => { var d = p - pl.Origin; return new Point2d(d * a, d * b); }).ToList();
      var h = Hull(p2);
      if (h.Count < 3) return a;
      double best = double.MaxValue; double bx = 1, by = 0;
      for (int i = 0; i < h.Count; i++)
      {
        var e = h[(i + 1) % h.Count] - h[i];
        double L = Math.Sqrt(e.X * e.X + e.Y * e.Y); if (L < 1e-12) continue;
        double ex = e.X / L, ey = e.Y / L;
        double s0 = double.MaxValue, s1 = double.MinValue, t0 = double.MaxValue, t1 = double.MinValue;
        foreach (var p in h)
        {
          double s = p.X * ex + p.Y * ey, t = -p.X * ey + p.Y * ex;
          if (s < s0) s0 = s; if (s > s1) s1 = s; if (t < t0) t0 = t; if (t > t1) t1 = t;
        }
        double area = (s1 - s0) * (t1 - t0);
        if (area < best) { best = area; bx = ex; by = ey; }
      }
      var u = bx * a + by * b; u.Unitize();
      return u;
    }

    static List<Point2d> Hull(List<Point2d> p)
    {
      var s = p.OrderBy(x => x.X).ThenBy(x => x.Y).ToList();
      if (s.Count < 3) return s;
      double Cr(Point2d o, Point2d a, Point2d b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
      var h = new List<Point2d>();
      foreach (var x in s) { while (h.Count >= 2 && Cr(h[h.Count - 2], h[h.Count - 1], x) <= 0) h.RemoveAt(h.Count - 1); h.Add(x); }
      int lo = h.Count + 1;
      for (int i = s.Count - 2; i >= 0; i--) { var x = s[i]; while (h.Count >= lo && Cr(h[h.Count - 2], h[h.Count - 1], x) <= 0) h.RemoveAt(h.Count - 1); h.Add(x); }
      h.RemoveAt(h.Count - 1);
      return h;
    }

    // Move each edge to the intersection line with the nearest fitting neighbour plane
    static void TrimEdges(int i, Rect[] res, double[][] orig, IList<Segment> segs, double gap, double cosTol)
    {
      var r = res[i]; var o = orig[i];
      var u = r.Frame.XAxis; var v = r.Frame.YAxis;
      // edge k: 0 = s0 (runs along v), 1 = s1, 2 = t0 (runs along u), 3 = t1
      double[] best = { double.MaxValue, double.MaxValue, double.MaxValue, double.MaxValue };
      double[] val = { o[0], o[1], o[2], o[3] };
      for (int j = 0; j < res.Length; j++)
      {
        if (j == i) continue;
        var nj = segs[j].Plane.Normal; nj.Unitize();
        var ni = segs[i].Plane.Normal; ni.Unitize();
        if (Math.Abs(ni * nj) > 0.9) continue;   // nearly parallel planes do not cut usefully
        if (!Rhino.Geometry.Intersect.Intersection.PlanePlane(segs[i].Plane, segs[j].Plane, out Line ln)) continue;
        var ld = ln.Direction; ld.Unitize();
        for (int k = 0; k < 4; k++)
        {
          var edgeDir = k < 2 ? v : u;
          if (Math.Abs(ld * edgeDir) < cosTol) continue;
          // edge midpoint and offset of the line along the edge normal
          double s = k == 0 ? o[0] : k == 1 ? o[1] : 0.5 * (o[0] + o[1]);
          double t = k == 2 ? o[2] : k == 3 ? o[3] : 0.5 * (o[2] + o[3]);
          var mid = r.P(s, t);
          var on = ln.ClosestPoint(mid, false);
          var d = on - r.Frame.Origin;
          double off = k < 2 ? d * u : d * v, cur = k < 2 ? s : t;
          double dist = Math.Abs(off - cur);
          if (dist > gap || dist >= best[k]) continue;
          // the neighbour must reach this line: point lies within its rectangle (plus gap)
          var rj = res[j]; var oj = orig[j];
          var dj = on - rj.Frame.Origin;
          double sj = dj * rj.Frame.XAxis, tj = dj * rj.Frame.YAxis;
          if (sj < oj[0] - gap || sj > oj[1] + gap || tj < oj[2] - gap || tj > oj[3] + gap) continue;
          best[k] = dist; val[k] = off; r.Neighbour[k] = segs[j].Id;
        }
      }
      for (int k = 0; k < 4; k++) r.Trimmed[k] = best[k] < double.MaxValue;
      if (val[0] < val[1]) { r.S0 = val[0]; r.S1 = val[1]; }
      if (val[2] < val[3]) { r.T0 = val[2]; r.T1 = val[3]; }
    }

    // Share of 40 x 40 raster cells that contain points
    static double Coverage(Rect r, List<Point3d> pts)
    {
      const int N = 40;
      double w = r.Width, h = r.Height;
      if (w <= 0 || h <= 0 || pts.Count == 0) return 0;
      var occ = new bool[N * N]; int c = 0;
      foreach (var p in pts)
      {
        var d = p - r.Frame.Origin;
        int a = (int)Math.Floor((d * r.Frame.XAxis - r.S0) / w * N), b = (int)Math.Floor((d * r.Frame.YAxis - r.T0) / h * N);
        if (a < 0 || b < 0 || a >= N || b >= N) continue;
        if (!occ[a * N + b]) { occ[a * N + b] = true; c++; }
      }
      return (double)c / (N * N);
    }
  }
}
