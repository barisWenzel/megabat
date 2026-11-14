// Megabat: computation core without Grasshopper dependency
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Rhino;
using Rhino.Geometry;

namespace Megabat.Core
{
  public static class SectionCore
  {
    public class Result
    {
      public List<Line> Lines = new List<Line>();
      public List<Polyline> Polys = new List<Polyline>();
      public List<Line> PlaneLines = new List<Line>();
      public PointCloud CutPts = new PointCloud(), Beyond = new PointCloud();
      public int Unused;
    }

    const int Block = 4000000;

    public static Result Run(PointCloud cloud, Plane pl, double thick, double depth, double tol, double minLen, double gap, bool trace,
      List<Plane> planes = null, List<PointCloud> segClouds = null)
    {
      var res = new Result();
      Vector3d n = pl.Normal; n.Unitize();
      Vector3d ax = pl.XAxis, ay = pl.YAxis; ax.Unitize(); ay.Unitize();
      Point3d o = pl.Origin;
      double h = thick / 2;
      bool hasCol = cloud.ContainsColors;

      // 1. Read band and view in blocks, in plane coordinates
      var cu = new List<double>(); var cv = new List<double>();
      int cnt = cloud.Count;
      for (int start = 0; start < cnt; start += Block)
      {
        int len = Math.Min(Block, cnt - start);
        var kind = new sbyte[len]; var uu = new double[len]; var vv = new double[len]; var ss = new double[len];
        var col = hasCol ? new Color[len] : null;
        Parallel.For(0, len, i =>
        {
          var it = cloud[start + i];
          var d = it.Location - o;
          double s = d * n;
          if (Math.Abs(s) <= h) kind[i] = 1;
          else if (depth > 0 ? (s > h && s <= depth) : (depth < 0 && s < -h && s >= depth)) kind[i] = 2;   // sign of depth selects the side
          else return;
          uu[i] = d * ax; vv[i] = d * ay; ss[i] = s;
          if (hasCol) col[i] = it.Color;
        });
        var bp = new List<Point3d>(); var bc = new List<Color>();
        var cpp = new List<Point3d>(); var cpc = new List<Color>();
        for (int i = 0; i < len; i++)
        {
          if (kind[i] == 0) continue;
          var q = o + ax * uu[i] + ay * vv[i];
          if (kind[i] == 1) { cu.Add(uu[i]); cv.Add(vv[i]); cpp.Add(q); if (hasCol) cpc.Add(col[i]); }
          else { bp.Add(q + n * ss[i]); if (hasCol) bc.Add(col[i]); }   // view in original position
        }
        if (hasCol) { res.CutPts.AddRange(cpp, cpc); res.Beyond.AddRange(bp, bc); }
        else { res.CutPts.AddRange(cpp); res.Beyond.AddRange(bp); }
      }

      // 2a. Intersect segment planes with the section plane, remaining points for step 2b/2c
      if (planes != null && segClouds != null && planes.Count > 0 && planes.Count == segClouds.Count)
      {
        var psegs = PlaneCuts(pl, planes, segClouds, thick, tol, minLen, gap);
        JoinCorners(psegs, gap);
        psegs.RemoveAll(q => q.A.DistanceTo(q.B) < minLen);
        foreach (var s in psegs)
          res.PlaneLines.Add(new Line(o + ax * s.A.X + ay * s.A.Y, o + ax * s.B.X + ay * s.B.Y));
        // Remove band points explained by a plane line
        double expl = Math.Max(2 * tol, 0.5 * thick);
        var keep = new List<int>();
        for (int i = 0; i < cu.Count; i++)
        {
          var q = new Point2d(cu[i], cv[i]);
          bool hit = false;
          foreach (var s in psegs) if (DistSeg(q, s.A, s.B) <= expl) { hit = true; break; }
          if (!hit) keep.Add(i);
        }
        cu = keep.Select(i => cu[i]).ToList();
        cv = keep.Select(i => cv[i]).ToList();
      }

      if (trace)
      {
        // 2b. Chain points like tracing by hand
        foreach (var ch in Trace(cu.ToArray(), cv.ToArray(), tol, Math.Max(gap, 2 * tol), minLen, out res.Unused))
        {
          var p3 = new Polyline(ch.Select(q => o + ax * q.X + ay * q.Y));
          res.Polys.Add(p3);
        }
        return res;
      }

      // 2. Find lines in the band
      var segs = FitLines(cu.ToArray(), cv.ToArray(), tol, minLen, gap, out res.Unused);

      // 3. Corners: pull ends of neighbouring lines to their intersection
      JoinCorners(segs, gap);
      segs.RemoveAll(q => q.A.DistanceTo(q.B) < minLen);   // pieces that became too short by trimming

      foreach (var s in segs)
        res.Lines.Add(new Line(o + ax * s.A.X + ay * s.A.Y, o + ax * s.B.X + ay * s.B.Y));
      return res;
    }

    public class Seg { public Point2d A, B; }

    // Intersect each plane with the section plane; trim the line to the segment points in the band
    // and split at gaps > gap
    static List<Seg> PlaneCuts(Plane sec, List<Plane> planes, List<PointCloud> segs, double thick, double tol, double minLen, double gap)
    {
      var res = new List<Seg>();
      Vector3d n = sec.Normal; n.Unitize();
      Vector3d ax = sec.XAxis, ay = sec.YAxis; ax.Unitize(); ay.Unitize();
      Point3d o = sec.Origin;
      double h = thick / 2, near = Math.Max(3 * tol, h);
      double bridge = Math.Max(gap, 3 * tol);
      for (int k = 0; k < planes.Count; k++)
      {
        var pk = planes[k]; var sc = segs[k];
        if (!pk.IsValid || sc == null || sc.Count == 0) continue;
        Vector3d m = pk.Normal; m.Unitize();
        if (Vector3d.CrossProduct(n, m).Length < Math.Sin(Rhino.RhinoMath.ToRadians(10))) continue;   // almost parallel
        Line L;
        if (!Rhino.Geometry.Intersect.Intersection.PlanePlane(sec, pk, out L)) continue;
        var a0 = L.From - o; var a1 = L.To - o;
        var A = new Point2d(a0 * ax, a0 * ay); var B = new Point2d(a1 * ax, a1 * ay);
        var d = B - A; if (d.Length < 1e-9) continue; d.Unitize();
        var nn = new Vector2d(-d.Y, d.X);
        var ts = new List<double>();
        int c = sc.Count;
        for (int i = 0; i < c; i++)
        {
          var q = sc[i].Location - o;
          if (Math.Abs(q * n) > h) continue;
          var p2 = new Point2d(q * ax, q * ay) - A;
          if (Math.Abs(p2 * nn) > near) continue;
          ts.Add(p2 * d);
        }
        if (ts.Count < 3) continue;
        ts.Sort();
        int s0 = 0;
        for (int i = 1; i <= ts.Count; i++)
        {
          if (i < ts.Count && ts[i] - ts[i - 1] <= bridge) continue;
          double t0 = ts[s0], t1 = ts[i - 1];
          if (t1 - t0 >= minLen && i - s0 >= 3) res.Add(new Seg { A = A + d * t0, B = A + d * t1 });
          s0 = i;
        }
      }
      return res;
    }

    static double DistSeg(Point2d p, Point2d a, Point2d b)
    {
      var ab = b - a; double l2 = ab.X * ab.X + ab.Y * ab.Y;
      double t = l2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, ((p - a) * ab) / l2));
      return p.DistanceTo(a + ab * t);
    }

    // Tracing: average points on a fine grid, then from an end point
    // always step to the next free neighbour within r, preferring straight ahead.
    // Points within tol of the current point count as done (band thickness).
    static List<List<Point2d>> Trace(double[] u, double[] v, double tol, double r, double minLen, out int unused)
    {
      var chains = new List<List<Point2d>>();
      // Thin: mean per cell of size tol
      double cs = Math.Max(tol, 1e-3);
      var cells = new Dictionary<long, (double x, double y, int n)>();
      for (int i = 0; i < u.Length; i++)
      {
        long k = Key((int)Math.Floor(u[i] / cs), (int)Math.Floor(v[i] / cs));
        cells.TryGetValue(k, out var c);
        cells[k] = (c.x + u[i], c.y + v[i], c.n + 1);
      }
      var pts = cells.Values.Select(c => new Point2d(c.x / c.n, c.y / c.n)).ToArray();
      int n = pts.Length;

      // Neighbour search via grid of size r
      var grid = new Dictionary<long, List<int>>();
      for (int i = 0; i < n; i++)
      {
        long k = Key((int)Math.Floor(pts[i].X / r), (int)Math.Floor(pts[i].Y / r));
        if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<int>();
        l.Add(i);
      }
      Func<Point2d, double, List<int>> near = (p, rad) =>
      {
        var res = new List<int>();
        int gx = (int)Math.Floor(p.X / r), gy = (int)Math.Floor(p.Y / r);
        for (int a = -1; a <= 1; a++) for (int b = -1; b <= 1; b++)
            if (grid.TryGetValue(Key(gx + a, gy + b), out var l))
              foreach (var j in l) if (pts[j].DistanceTo(p) <= rad) res.Add(j);
        return res;
      };

      // Start at points with few neighbours (ends first)
      var deg = new int[n];
      Parallel.For(0, n, i => deg[i] = near(pts[i], r).Count);
      var order = Enumerable.Range(0, n).OrderBy(i => deg[i]).ToArray();
      var done = new bool[n];
      double absorb = Math.Max(tol, cs) * 1.5;

      foreach (var s in order)
      {
        if (done[s]) continue;
        var fwd = Walk(s, pts, done, near, r, absorb, true);
        // also extend backwards from the start
        var back = Walk(s, pts, done, near, r, absorb, false, fwd.Count > 1 ? pts[fwd[1]] - pts[fwd[0]] : Vector2d.Unset);
        back.Reverse();
        var ch = back.Take(back.Count - 1).Concat(fwd).Select(i => pts[i]).ToList();
        if (ch.Count < 2) continue;
        var pl = new Polyline(ch.Select(q => new Point3d(q.X, q.Y, 0)));
        if (pl.Length < minLen) continue;
        pl.ReduceSegments(tol);
        chains.Add(pl.Select(q => new Point2d(q.X, q.Y)).ToList());
      }
      unused = done.Count(d => !d);
      return chains;
    }

    static List<int> Walk(int s, Point2d[] pts, bool[] done, Func<Point2d, double, List<int>> near,
      double r, double absorb, bool forward, Vector2d avoid = default(Vector2d))
    {
      var path = new List<int> { s };
      foreach (var j in near(pts[s], absorb)) done[j] = true;
      done[s] = true;
      Vector2d dir = Vector2d.Unset;
      if (!forward && avoid.IsValid) { dir = -avoid; dir.Unitize(); }
      int cur = s;
      while (true)
      {
        int best = -1; double bestScore = double.MaxValue;
        foreach (var j in near(pts[cur], r))
        {
          if (done[j]) continue;
          var d = pts[j] - pts[cur];
          double len = d.Length;
          if (len < 1e-9) { done[j] = true; continue; }
          double score = len / r;
          if (dir.IsValid)
          {
            double cos = (d * dir) / len;
            if (cos < -0.3) continue;              // no U-turn
            score += 0.6 * (1 - cos);              // prefer straight ahead
          }
          if (score < bestScore) { bestScore = score; best = j; }
        }
        if (best < 0) break;
        var step = pts[best] - pts[cur]; step.Unitize();
        dir = dir.IsValid ? 0.5 * dir + 0.5 * step : step; dir.Unitize();
        cur = best;
        path.Add(cur);
        foreach (var j in near(pts[cur], absorb)) done[j] = true;
      }
      return path;
    }

    static long Key(int x, int y) { return ((long)x << 32) | ((long)y & 0xFFFFFFFFL); }

    // Sequential 2D RANSAC; each line is split into segments at gaps > gap
    static List<Seg> FitLines(double[] u, double[] v, double tol, double minLen, double gap, out int unused)
    {
      var segs = new List<Seg>();
      int n = u.Length;
      var alive = Enumerable.Range(0, n).ToList();
      var rnd = new Random(1);
      int minPts = 8, misses = 0;
      double bridge = Math.Max(gap, 3 * tol);

      for (int round = 0; round < 400 && alive.Count >= minPts && misses < 15; round++)
      {
        // Sample for scoring
        int m = Math.Min(alive.Count, 15000);
        var smp = new int[m];
        for (int i = 0; i < m; i++) smp[i] = alive[m == alive.Count ? i : rnd.Next(alive.Count)];
        int iters = 250;
        var pa = new int[iters]; var pb = new int[iters];
        for (int k = 0; k < iters; k++) { pa[k] = alive[rnd.Next(alive.Count)]; pb[k] = alive[rnd.Next(alive.Count)]; }
        var score = new int[iters];
        Parallel.For(0, iters, k =>
        {
          double dx = u[pb[k]] - u[pa[k]], dy = v[pb[k]] - v[pa[k]];
          double L = Math.Sqrt(dx * dx + dy * dy);
          if (L < 2 * tol) return;
          double nx = -dy / L, ny = dx / L, c = -(nx * u[pa[k]] + ny * v[pa[k]]);
          int s = 0;
          for (int j = 0; j < m; j++) if (Math.Abs(nx * u[smp[j]] + ny * v[smp[j]] + c) <= tol) s++;
          score[k] = s;
        });
        int best = 0;
        for (int k = 1; k < iters; k++) if (score[k] > score[best]) best = k;
        if (score[best] * (double)alive.Count / m < minPts) break;

        // Fit line to the inliers (PCA), twice
        double bx = u[pb[best]] - u[pa[best]], by = v[pb[best]] - v[pa[best]];
        double bl = Math.Sqrt(bx * bx + by * by);
        double lnx = -by / bl, lny = bx / bl, lc = -(lnx * u[pa[best]] + lny * v[pa[best]]);
        List<int> inl = null;
        double mx = 0, my = 0, dxv = 1, dyv = 0;
        for (int rep = 0; rep < 2; rep++)
        {
          inl = alive.Where(i => Math.Abs(lnx * u[i] + lny * v[i] + lc) <= tol).ToList();
          if (inl.Count < 2) break;
          mx = inl.Average(i => u[i]); my = inl.Average(i => v[i]);
          double sxx = 0, syy = 0, sxy = 0;
          foreach (var i in inl) { double a = u[i] - mx, b = v[i] - my; sxx += a * a; syy += b * b; sxy += a * b; }
          double ang = 0.5 * Math.Atan2(2 * sxy, sxx - syy);
          dxv = Math.Cos(ang); dyv = Math.Sin(ang);
          lnx = -dyv; lny = dxv; lc = -(lnx * mx + lny * my);
        }
        if (inl == null || inl.Count < minPts) { misses++; continue; }

        // Sort along the line and split at gaps
        var ts = inl.Select(i => (t: (u[i] - mx) * dxv + (v[i] - my) * dyv, i)).OrderBy(x => x.t).ToList();
        var used = new HashSet<int>();
        int s0 = 0; bool any = false;
        for (int k = 1; k <= ts.Count; k++)
        {
          if (k < ts.Count && ts[k].t - ts[k - 1].t <= bridge) continue;
          double t0 = ts[s0].t, t1 = ts[k - 1].t;
          int count = k - s0;
          if (t1 - t0 >= minLen && count >= minPts)
          {
            segs.Add(new Seg { A = new Point2d(mx + dxv * t0, my + dyv * t0), B = new Point2d(mx + dxv * t1, my + dyv * t1) });
            for (int q = s0; q < k; q++) used.Add(ts[q].i);
            any = true;
          }
          s0 = k;
        }
        // Remove all inliers of this line so it is not found again
        var drop = new HashSet<int>(inl);
        alive = alive.Where(i => !drop.Contains(i)).ToList();
        misses = any ? 0 : misses + 1;
      }
      unused = alive.Count;
      return segs;
    }

    // If two ends are closer than gap and the lines are not parallel,
    // both are extended or trimmed to the intersection of the lines
    static void JoinCorners(List<Seg> segs, double gap)
    {
      if (gap <= 0) return;
      for (int a = 0; a < segs.Count; a++)
        for (int b = a + 1; b < segs.Count; b++)
          for (int ea = 0; ea < 2; ea++)
            for (int eb = 0; eb < 2; eb++)
            {
              var A = segs[a]; var B = segs[b];
              Point2d pa = ea == 0 ? A.A : A.B, pb = eb == 0 ? B.A : B.B;
              if (pa.DistanceTo(pb) > gap) continue;
              Point2d x;
              if (!Intersect(A.A, A.B, B.A, B.B, out x)) continue;
              if (x.DistanceTo(pa) > gap || x.DistanceTo(pb) > gap) continue;
              if (ea == 0) A.A = x; else A.B = x;
              if (eb == 0) B.A = x; else B.B = x;
            }
    }

    static bool Intersect(Point2d a0, Point2d a1, Point2d b0, Point2d b1, out Point2d x)
    {
      x = Point2d.Unset;
      double d1x = a1.X - a0.X, d1y = a1.Y - a0.Y, d2x = b1.X - b0.X, d2y = b1.Y - b0.Y;
      double den = d1x * d2y - d1y * d2x;
      double l1 = Math.Sqrt(d1x * d1x + d1y * d1y), l2 = Math.Sqrt(d2x * d2x + d2y * d2y);
      if (l1 < 1e-9 || l2 < 1e-9 || Math.Abs(den) / (l1 * l2) < Math.Sin(Rhino.RhinoMath.ToRadians(15))) return false;
      double t = ((b0.X - a0.X) * d2y - (b0.Y - a0.Y) * d2x) / den;
      x = new Point2d(a0.X + t * d1x, a0.Y + t * d1y);
      return true;
    }
  }
}
