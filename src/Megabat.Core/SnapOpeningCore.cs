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
  public static class SnapOpeningCore
  {
    public class Result
    {
      public Polyline Rect;
      public Brep Opening;
      public List<double> Size = new List<double>();
      public double Depth = double.NaN;
      public List<double> Dev = new List<double>();
      public List<string> Info = new List<string>();
    }

    public static Result Run(Polyline g, List<Plane> P, List<int> T, PointCloud cloud,
      double dTol, double search, double cell, double minStep, double tol)
    {
      var res = new Result();
      if (cell <= 0 || search <= 0) { res.Info.Add("cell and search must be greater than 0"); return res; }

      // Host wall: vertical plane with the smallest maximum distance to the sketched points
      int host = -1; double hostD = double.MaxValue;
      for (int i = 0; i < P.Count; i++)
      {
        if (T[i] != 1) continue;
        double dm = 0;
        foreach (var q in g) dm = Math.Max(dm, Math.Abs(P[i].DistanceTo(q)));
        if (dm < hostD) { hostD = dm; host = i; }
      }
      // No plane close enough to the sketch: fit the wall locally from the points around the window
      Plane hp = host >= 0 ? P[host] : Plane.Unset;
      if (host < 0 || hostD > search)
      {
        Plane lp; int lc;
        if (LocalWall(g, cloud, search, out lp, out lc))
        {
          double dm = 0;
          foreach (var q in g) dm = Math.Max(dm, Math.Abs(lp.DistanceTo(q)));
          res.Info.Add(string.Format("Host wall: fitted locally from C, points={0}, max. distance of sketch={1:F3}" +
            (host >= 0 ? " (nearest plane {2} was {3:F3} away)" : ""), lc, dm, host, hostD));
          hp = lp; host = -2;
        }
        else if (host < 0) { res.Info.Add("no vertical plane found and no local wall in C"); return res; }
      }

      // Wall system: u horizontal along the wall, v = Z, w = normal
      Vector3d n = hp.Normal; n.Z = 0; n.Unitize();
      var u = new Vector3d(-n.Y, n.X, 0);
      var v = Vector3d.ZAxis;
      Point3d o = hp.Origin;
      if (host >= 0) res.Info.Add(string.Format("Host wall: plane {0}, max. distance of sketch={1:F3}", host, hostD));

      // Sketched rectangle in (u, v)
      double u0 = double.MaxValue, u1 = double.MinValue, v0 = double.MaxValue, v1 = double.MinValue;
      foreach (var q in g)
      {
        var d = q - o;
        double a = d * u, b = d * v;
        u0 = Math.Min(u0, a); u1 = Math.Max(u1, a); v0 = Math.Min(v0, b); v1 = Math.Max(v1, b);
      }

      // Occupancy grid in the band around the wall plane
      double ua = u0 - search - cell, va = v0 - search - cell;
      int nu = (int)Math.Ceiling((u1 - u0 + 2 * search + 2 * cell) / cell) + 1;
      int nv = (int)Math.Ceiling((v1 - v0 + 2 * search + 2 * cell) / cell) + 1;
      var occ = new bool[nu, nv];
      var behind = new List<(double a, double b, double w)>();
      var band = new List<(double a, double b)>();
      foreach (var q in cloud.GetPoints())
      {
        var d = q - o;
        double a = d * u, b = d * v, w = d * n;
        int iu = (int)Math.Floor((a - ua) / cell), iv = (int)Math.Floor((b - va) / cell);
        if (iu < 0 || iv < 0 || iu >= nu || iv >= nv) continue;
        if (Math.Abs(w) <= dTol) { occ[iu, iv] = true; band.Add((a, b)); }
        else if (Math.Abs(w) < 1.0) behind.Add((a, b, w));
      }

      // Edges one by one: occupancy jump across the edge, averaged over the middle part of the edge
      int k = 3;
      double L, R, B, Tp; double sL, sR, sB, sT;
      L  = Edge(occ, nu, nv, ua, va, cell, k, true,  u0, v0 + 0.2 * (v1 - v0), v1 - 0.2 * (v1 - v0), search, +1, out sL);
      R  = Edge(occ, nu, nv, ua, va, cell, k, true,  u1, v0 + 0.2 * (v1 - v0), v1 - 0.2 * (v1 - v0), search, -1, out sR);
      B  = Edge(occ, nu, nv, ua, va, cell, k, false, v0, u0 + 0.2 * (u1 - u0), u1 - 0.2 * (u1 - u0), search, +1, out sB);
      Tp = Edge(occ, nu, nv, ua, va, cell, k, false, v1, u0 + 0.2 * (u1 - u0), u1 - 0.2 * (u1 - u0), search, -1, out sT);

      // Point-exact: outermost wall point at the grid edge (the grid is only accurate to about one cell)
      L  = Refine(band, true,  L,  v0 + 0.2 * (v1 - v0), v1 - 0.2 * (v1 - v0), cell, +1);
      R  = Refine(band, true,  R,  v0 + 0.2 * (v1 - v0), v1 - 0.2 * (v1 - v0), cell, -1);
      B  = Refine(band, false, B,  u0 + 0.2 * (u1 - u0), u1 - 0.2 * (u1 - u0), cell, +1);
      Tp = Refine(band, false, Tp, u0 + 0.2 * (u1 - u0), u1 - 0.2 * (u1 - u0), cell, -1);

      string[] names = { "left", "right", "bottom", "top" };
      double[] drawn = { u0, u1, v0, v1 };
      double[] found = { L, R, B, Tp };
      double[] step = { sL, sR, sB, sT };
      for (int i = 0; i < 4; i++)
      {
        bool ok = step[i] >= minStep;
        if (!ok) found[i] = drawn[i];
        res.Dev.Add(ok ? found[i] - drawn[i] : double.NaN);
        res.Info.Add(string.Format("Edge {0}: {1}, step={2:F2}, shift={3}", names[i], ok ? "found" : "as drawn",
          step[i], ok ? (found[i] - drawn[i]).ToString("F3") : "-"));
      }
      L = found[0]; R = found[1]; B = found[2]; Tp = found[3];

      // Rectangle in the wall plane
      Func<double, double, Point3d> W = (a, b) => o + a * u + b * v;
      res.Rect = new Polyline(new[] { W(L, B), W(R, B), W(R, Tp), W(L, Tp), W(L, B) });

      // Reveal depth: points behind the wall plane inside the opening
      double m = 2 * cell;
      var depths = new List<double>(); int pos = 0, neg = 0;
      foreach (var p in behind)
      {
        if (p.a < L + m || p.a > R - m || p.b < B + m || p.b > Tp - m) continue;
        depths.Add(p.w);
        if (p.w > 0) pos++; else neg++;
      }
      double side = pos >= neg ? 1 : -1;
      var dd = new List<double>();
      foreach (var w in depths) if (w * side > 0) dd.Add(Math.Abs(w));
      dd.Sort();
      double depth = dd.Count >= 10 ? dd[(int)(0.95 * (dd.Count - 1))] : double.NaN;
      res.Depth = depth;
      res.Info.Add(dd.Count >= 10
        ? string.Format("Reveal: {0} points, depth={1:F3} on the {2} side of the wall normal", dd.Count, depth, side > 0 ? "+" : "-")
        : "Reveal: too few points behind the wall plane");

      // Opening volume: from the wall plane to the reveal depth
      double front = 0, back = double.IsNaN(depth) ? dTol : depth;
      var bp = new Plane(o, u, v);
      var box = new Box(bp, new Interval(L, R), new Interval(B, Tp),
        side > 0 ? new Interval(-front, back) : new Interval(-back, front));
      res.Opening = box.ToBrep();

      // Sill height: highest horizontal plane below the bottom edge
      double zB = W(L, B).Z, floor = double.NaN;
      for (int i = 0; i < P.Count; i++)
        if (T[i] == 0 && P[i].OriginZ < zB - tol && (double.IsNaN(floor) || P[i].OriginZ > floor)) floor = P[i].OriginZ;
      res.Size.Add(R - L);
      res.Size.Add(Tp - B);
      res.Size.Add(double.IsNaN(floor) ? double.NaN : zB - floor);
      return res;
    }

    // Outermost wall point near grid edge e; dir = +1: wall at smaller coordinates
    // Vertical wall plane from the points around the sketch (frame up to search, window area excluded):
    // densest layer along the sketch normal, best-fit line in plan within it
    private static bool LocalWall(Polyline g, PointCloud cloud, double search, out Plane pl, out int cnt)
    {
      pl = Plane.Unset; cnt = 0;
      Plane gp;
      if (Plane.FitPlaneToPoints(g, out gp) != PlaneFitResult.Success) return false;
      Vector3d n = gp.Normal; n.Z = 0;
      if (!n.Unitize()) return false;
      var u = new Vector3d(-n.Y, n.X, 0);
      Point3d o = gp.Origin;
      double u0 = double.MaxValue, u1 = double.MinValue, v0 = double.MaxValue, v1 = double.MinValue;
      foreach (var q in g)
      {
        var d = q - o; double a = d * u, b = d.Z;
        u0 = Math.Min(u0, a); u1 = Math.Max(u1, a); v0 = Math.Min(v0, b); v1 = Math.Max(v1, b);
      }
      var sel = new List<(Point3d p, double w)>();
      int m = cloud.Count;
      for (int i = 0; i < m; i++)
      {
        var q = cloud[i].Location; var d = q - o;
        double a = d * u, b = d.Z, w = d * n;
        if (Math.Abs(w) >= 1.0 || a < u0 - search || a > u1 + search || b < v0 - search || b > v1 + search) continue;
        if (a > u0 && a < u1 && b > v0 && b < v1) continue;   // window area
        sel.Add((q, w));
      }
      if (sel.Count < 50) return false;
      // Layer with the most points (2 cm grid)
      var hist = new int[100];
      foreach (var s in sel) hist[Math.Min(99, (int)((s.w + 1.0) / 0.02))]++;
      int k = 0; for (int i = 1; i < 100; i++) if (hist[i] > hist[k]) k = i;
      double wc = -1.0 + (k + 0.5) * 0.02;
      double cx = 0, cy = 0, cz = 0; var inl = new List<Point3d>();
      foreach (var s in sel) if (Math.Abs(s.w - wc) < 0.05) { inl.Add(s.p); cx += s.p.X; cy += s.p.Y; cz += s.p.Z; }
      if (inl.Count < 30) return false;
      cx /= inl.Count; cy /= inl.Count; cz /= inl.Count;
      double sxx = 0, syy = 0, sxy = 0;
      foreach (var q in inl) { double dx = q.X - cx, dy = q.Y - cy; sxx += dx * dx; syy += dy * dy; sxy += dx * dy; }
      double ang = 0.5 * Math.Atan2(2 * sxy, sxx - syy);
      var dir = new Vector3d(Math.Cos(ang), Math.Sin(ang), 0);
      pl = new Plane(new Point3d(cx, cy, cz), dir, Vector3d.ZAxis);
      if (pl.Normal * n < 0) pl = new Plane(new Point3d(cx, cy, cz), -dir, Vector3d.ZAxis);   // same direction as the sketch
      cnt = inl.Count;
      return true;
    }

    private static double Refine(List<(double a, double b)> pts, bool vertical, double e, double s0, double s1, double cell, int dir)
    {
      double best = double.NaN;
      foreach (var p in pts)
      {
        double x = vertical ? p.a : p.b, y = vertical ? p.b : p.a;
        if (y < s0 || y > s1) continue;
        if (dir > 0)
        {
          if (x > e + 2 * cell || x < e - 4 * cell) continue;
          if (double.IsNaN(best) || x > best) best = x;
        }
        else
        {
          if (x < e - 2 * cell || x > e + 4 * cell) continue;
          if (double.IsNaN(best) || x < best) best = x;
        }
      }
      return double.IsNaN(best) ? e : best;
    }

    // Finds the edge within drawn ± search. vertical = edge runs in v direction (left/right).
    // dir = +1: wall on the side of smaller coordinates, opening on the larger; dir = -1 the other way round.
    private static double Edge(bool[,] occ, int nu, int nv, double ua, double va, double cell, int k,
      bool vertical, double drawn, double s0, double s1, double search, int dir, out double bestStep)
    {
      double a0 = vertical ? ua : va;
      int nAcross = vertical ? nu : nv;
      double sa = vertical ? va : ua;
      int i0 = Math.Max(0, (int)Math.Floor((s0 - sa) / cell));
      int i1 = Math.Min((vertical ? nv : nu) - 1, (int)Math.Floor((s1 - sa) / cell));

      // Occupancy per column (or row) over the middle part of the edge
      var f = new double[nAcross];
      for (int c = 0; c < nAcross; c++)
      {
        int cnt = 0, tot = 0;
        for (int j = i0; j <= i1; j++) { tot++; if (vertical ? occ[c, j] : occ[j, c]) cnt++; }
        f[c] = tot > 0 ? (double)cnt / tot : 0;
      }

      int cMin = Math.Max(k, (int)Math.Floor((drawn - search - a0) / cell));
      int cMax = Math.Min(nAcross - k, (int)Math.Ceiling((drawn + search - a0) / cell));
      double best = drawn; bestStep = 0; int bc = -1;
      for (int c = cMin; c <= cMax; c++)
      {
        double lo = 0, hi = 0;
        for (int j = 1; j <= k; j++) { lo += f[c - j]; hi += f[c + j - 1]; }
        double step = dir * (lo - hi) / k;   // wall before the boundary, opening behind
        if (step > bestStep) { bestStep = step; best = a0 + c * cell; bc = c; }
      }
      if (bc < 0) return best;

      // Finer: position where the occupancy crosses halfway between wall and opening level
      // (compensates for partially covered border cells counting as occupied)
      double fw = 0, fo = 1;
      for (int j = 1; j <= k; j++)
      {
        int iw = dir > 0 ? bc - j : bc + j - 1, io = dir > 0 ? bc + j - 1 : bc - j;
        if (iw >= 0 && iw < nAcross) fw = Math.Max(fw, f[iw]);
        if (io >= 0 && io < nAcross) fo = Math.Min(fo, f[io]);
      }
      double mid = 0.5 * (fw + fo);
      int i = dir > 0 ? bc - 3 : bc + 2;
      for (int step = 0; step < 6; step++)
      {
        int nx = i + dir;
        if (i < 0 || nx < 0 || i >= nAcross || nx >= nAcross) break;
        if (f[i] >= mid && f[nx] < mid)
        {
          double ci = a0 + (i + 0.5) * cell;
          return ci + dir * cell * (f[i] - mid) / (f[i] - f[nx]);
        }
        i = nx;
      }
      return best;
    }
  }
}
