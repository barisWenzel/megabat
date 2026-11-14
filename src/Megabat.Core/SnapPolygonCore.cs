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
  public static class SnapPolygonCore
  {
    public class Result
    {
      public Polyline Polygon;
      public Brep Room;
      public List<int> Wall = new List<int>();
      public List<double> Dev = new List<double>();
      public List<double> Plumb = new List<double>();
      public List<double> Flat = new List<double>();
      public List<string> Info = new List<string>();
    }

    // Wall in plan: point, direction, normal, occupied interval along the direction
    private class W2
    {
      public int Index;
      public Vector2d O, D, N;
      public double[] T;   // sorted projections of the sample points onto D
    }

    // Line in plan
    private struct Ln
    {
      public Vector2d O, D;
    }

    public static Result Run(Polyline input, List<Plane> P, List<int> T, List<PointCloud> S, PointCloud cloud,
      double angTol, double dTol, double minSup, int sample, double tol,
      bool open = false, double zBotIn = double.NaN, double zTopIn = double.NaN)
    {
      var res = new Result();

      // Prepare walls
      var walls = new List<W2>();
      for (int i = 0; i < P.Count; i++)
      {
        if (T[i] != 1) continue;
        var n = new Vector2d(P[i].Normal.X, P[i].Normal.Y);
        if (n.Length < 1e-9) continue;
        n.Unitize();
        var w = new W2 { Index = i, O = new Vector2d(P[i].OriginX, P[i].OriginY), N = n, D = new Vector2d(-n.Y, n.X) };
        var pts = S[i].GetPoints();
        int st = Math.Max(1, pts.Length / Math.Max(1, sample));
        var t = new List<double>(pts.Length / st + 1);
        for (int k = 0; k < pts.Length; k += st)
          t.Add((pts[k].X - w.O.X) * w.D.X + (pts[k].Y - w.O.Y) * w.D.Y);
        t.Sort();
        w.T = t.ToArray();
        walls.Add(w);
      }

      // Polygon points in plan, without closing point
      var v = new List<Vector2d>();
      for (int i = 0; i < input.Count; i++) v.Add(new Vector2d(input[i].X, input[i].Y));
      if (v.Count > 1 && (v[0] - v[v.Count - 1]).Length < tol) v.RemoveAt(v.Count - 1);
      int m = v.Count;
      if (!open && m < 3) { res.Info.Add("Polygon has fewer than three corners"); return res; }
      if (open && m < 2) { res.Info.Add("Polyline has fewer than two points"); return res; }
      int mE = open ? m - 1 : m;   // number of edges

      // Floor and ceiling: lowest and highest horizontal plane with points inside the polygon
      double zF = double.NaN, zC = double.NaN;
      for (int i = 0; i < P.Count; i++)
      {
        if (T[i] != 0) continue;
        var pts = S[i].GetPoints();
        int st = Math.Max(1, pts.Length / Math.Max(1, sample));
        int tot = 0, inside = 0;
        for (int k = 0; k < pts.Length; k += st)
        {
          tot++;
          if (Inside(v, pts[k].X, pts[k].Y)) inside++;
        }
        if (tot == 0 || inside < 0.2 * tot) continue;
        double zi = P[i].OriginZ;
        if (double.IsNaN(zF) || zi < zF) zF = zi;
        if (double.IsNaN(zC) || zi > zC) zC = zi;
      }
      bool single = !double.IsNaN(zF) && zF == zC;   // only one horizontal plane: floor or roof?
      if (open) { zF = double.NaN; zC = double.NaN; single = false; }   // open wall run: height from segment or input
      double zSingle = single ? zF : double.NaN;
      if (single) { zF = double.NaN; zC = double.NaN; }

      // Inputs take precedence; anything missing comes from the height of the largest segment
      var notes = new List<string>();   // append at the end, Info[e] belongs to edge e
      if (!double.IsNaN(zBotIn)) zF = zBotIn;
      if (!double.IsNaN(zTopIn)) zC = zTopIn;
      if (double.IsNaN(zF) || double.IsNaN(zC))
      {
        int big = -1;
        for (int i = 0; i < S.Count; i++) if (S[i] != null && (big < 0 || S[i].Count > S[big].Count)) big = i;
        if (big >= 0 && S[big].Count > 0)
        {
          var zs = new List<double>();
          foreach (var p in S[big].GetPoints()) zs.Add(p.Z);
          zs.Sort();
          double lo = zs[(int)(0.005 * (zs.Count - 1))], hi = zs[(int)(0.995 * (zs.Count - 1))];
          if (single)
          {
            // the single plane is at the top (roof/ceiling) or bottom (floor), inputs take precedence
            if (zSingle > 0.5 * (lo + hi)) { if (double.IsNaN(zC)) { zC = zSingle; notes.Add(string.Format("Horizontal plane at {0:F3} used as top", zSingle)); } }
            else if (double.IsNaN(zF)) { zF = zSingle; notes.Add(string.Format("Horizontal plane at {0:F3} used as bottom", zSingle)); }
          }
          if (double.IsNaN(zF)) { zF = lo; notes.Add(string.Format("Bottom from segment {0}: {1:F3}", big, lo)); }
          if (double.IsNaN(zC)) { zC = hi; notes.Add(string.Format("Top from segment {0}: {1:F3}", big, hi)); }
        }
      }

      double sinT = Math.Sin(angTol * Math.PI / 180.0);
      var lines = new Ln[mE];

      // Wall points for local fits: middle height band between floor and ceiling
      var band = new List<Vector2d>();
      if (cloud != null && cloud.Count > 0)
      {
        bool hasZ = !double.IsNaN(zF) && !double.IsNaN(zC);
        double z0 = hasZ ? zF + 0.15 * (zC - zF) : double.MinValue;
        double z1 = hasZ ? zC - 0.15 * (zC - zF) : double.MaxValue;
        foreach (var p in cloud.GetPoints())
          if (p.Z > z0 && p.Z < z1) band.Add(new Vector2d(p.X, p.Y));
      }

      // Wall points with height for the best-fit planes (without junctions to floor and ceiling)
      var wallPts = new List<Point3d>();
      bool hasH = !double.IsNaN(zF) && !double.IsNaN(zC);
      if (cloud != null && cloud.Count > 0 && hasH)
      {
        double h = zC - zF;
        foreach (var p in cloud.GetPoints())
          if (p.Z > zF + 0.1 * h && p.Z < zC - 0.1 * h) wallPts.Add(p);
      }

      // Assign edges to walls
      for (int e = 0; e < mE; e++)
      {
        Vector2d a = v[e], b = v[(e + 1) % m];
        Vector2d de = b - a;
        double len = de.Length;
        de.Unitize();
        Vector2d mid = 0.5 * (a + b);

        int best = -1; double bestD = double.MaxValue; double bestSup = 0;
        int near = -1; double nearD = double.MaxValue, nearSup = 0;
        for (int k = 0; k < walls.Count; k++)
        {
          var w = walls[k];
          if (Math.Abs(de.X * w.N.X + de.Y * w.N.Y) > sinT) continue;          // angle
          double d = Math.Abs((mid.X - w.O.X) * w.N.X + (mid.Y - w.O.Y) * w.N.Y);
          double ta = (a.X - w.O.X) * w.D.X + (a.Y - w.O.Y) * w.D.Y;
          double tb = (b.X - w.O.X) * w.D.X + (b.Y - w.O.Y) * w.D.Y;
          double sup = Support(w.T, Math.Min(ta, tb), Math.Max(ta, tb), len);
          if (d < nearD) { nearD = d; near = k; nearSup = sup; }               // for diagnostics

          if (d > dTol) continue;                                               // distance
          if (sup < minSup) continue;                                           // coverage

          if (d < bestD) { bestD = d; best = k; bestSup = sup; }
        }

        if (best >= 0)
        {
          var w = walls[best];
          // Turn wall direction to edge direction
          Vector2d dir = (w.D.X * de.X + w.D.Y * de.Y) >= 0 ? w.D : -w.D;
          lines[e] = new Ln { O = w.O, D = dir };
          double sd = (mid.X - w.O.X) * w.N.X + (mid.Y - w.O.Y) * w.N.Y;
          res.Wall.Add(w.Index);
          res.Dev.Add(Math.Abs(sd));
          res.Info.Add(string.Format("Edge {0}: wall {1}, distance={2:F3}, support={3:F2}", e, w.Index, Math.Abs(sd), bestSup));
        }
        else if (FitLocal(band, a, de, len, dTol, sinT, out Ln fit, out int nFit))
        {
          lines[e] = fit;
          var fn = new Vector2d(-fit.D.Y, fit.D.X);
          double sd = (mid.X - fit.O.X) * fn.X + (mid.Y - fit.O.Y) * fn.Y;
          res.Wall.Add(-2);
          res.Dev.Add(Math.Abs(sd));
          res.Info.Add(string.Format("Edge {0}: fitted locally, points={1}, distance={2:F3}", e, nFit, Math.Abs(sd)));
        }
        else
        {
          lines[e] = new Ln { O = a, D = de };
          res.Wall.Add(-1);
          res.Dev.Add(double.NaN);
          if (near >= 0)
            res.Info.Add(string.Format("Edge {0}: no wall (nearest parallel: plane {1}, distance={2:F3}, support={3:F2})", e, walls[near].Index, nearD, nearSup));
          else
            res.Info.Add(string.Format("Edge {0}: no wall (no parallel plane)", e));
        }
      }

      // Best-fit line per assigned edge from all wall points in the band,
      // plus plumb deviation (top against bottom) and flatness
      for (int e = 0; e < mE; e++)
      {
        res.Plumb.Add(double.NaN);
        res.Flat.Add(double.NaN);
        if (res.Wall[e] == -1 || wallPts.Count == 0) continue;

        Ln l = lines[e];
        var ln = new Vector2d(-l.D.Y, l.D.X);
        Vector2d a = v[e], b = v[(e + 1) % m];
        double ta = (a.X - l.O.X) * l.D.X + (a.Y - l.O.Y) * l.D.Y;
        double tb = (b.X - l.O.X) * l.D.X + (b.Y - l.O.Y) * l.D.Y;
        double t0 = Math.Min(ta, tb), t1 = Math.Max(ta, tb), lenE = t1 - t0;
        t0 += 0.1 * lenE; t1 -= 0.1 * lenE;
        double bw = Math.Min(dTol, Math.Min(0.1, 0.5 * lenE));   // band width: narrow for short edges

        var sel = new List<Point3d>(); var off = new List<double>();
        foreach (var p in wallPts)
        {
          double t = (p.X - l.O.X) * l.D.X + (p.Y - l.O.Y) * l.D.Y;
          if (t < t0 || t > t1) continue;
          double w = (p.X - l.O.X) * ln.X + (p.Y - l.O.Y) * ln.Y;
          if (Math.Abs(w) > bw) continue;
          sel.Add(p); off.Add(w);
        }
        if (sel.Count < 20) continue;

        // Remove outliers (furniture, reveals) via median and MAD, bulges and tilt remain
        var so = new List<double>(off); so.Sort();
        double med = so[so.Count / 2];
        var dev = new List<double>(); foreach (var w in off) dev.Add(Math.Abs(w - med)); dev.Sort();
        double thr = Math.Max(0.03, 3 * 1.4826 * dev[dev.Count / 2]);
        double sx = 0, sy = 0; int nn = 0;
        for (int i = 0; i < sel.Count; i++) if (Math.Abs(off[i] - med) <= thr) { sx += sel[i].X; sy += sel[i].Y; nn++; }
        if (nn < 20) continue;
        var mean = new Vector2d(sx / nn, sy / nn);
        double cxx = 0, cxy = 0, cyy = 0;
        for (int i = 0; i < sel.Count; i++)
        {
          if (Math.Abs(off[i] - med) > thr) continue;
          double dx = sel[i].X - mean.X, dy = sel[i].Y - mean.Y;
          cxx += dx * dx; cxy += dx * dy; cyy += dy * dy;
        }
        double ang = 0.5 * Math.Atan2(2 * cxy, cxx - cyy);
        var dir = new Vector2d(Math.Cos(ang), Math.Sin(ang));
        if (dir.X * l.D.X + dir.Y * l.D.Y < 0) dir = -dir;
        if (Math.Abs(dir.X * l.D.Y - dir.Y * l.D.X) > sinT) dir = l.D;
        lines[e] = new Ln { O = mean, D = dir };

        // Measures relative to the best-fit line
        var nn2 = new Vector2d(-dir.Y, dir.X);
        double h = zC - zF;
        var r2 = new List<double>(); var lo = new List<double>(); var hi = new List<double>();
        for (int i = 0; i < sel.Count; i++)
        {
          if (Math.Abs(off[i] - med) > thr) continue;
          double w = (sel[i].X - mean.X) * nn2.X + (sel[i].Y - mean.Y) * nn2.Y;
          r2.Add(w);
          if (sel[i].Z < zF + 0.3 * h) lo.Add(w);
          else if (sel[i].Z > zC - 0.3 * h) hi.Add(w);
        }
        r2.Sort(); lo.Sort(); hi.Sort();
        double flat = r2[(int)(0.95 * (r2.Count - 1))] - r2[(int)(0.05 * (r2.Count - 1))];
        double plumb = (lo.Count >= 10 && hi.Count >= 10) ? hi[hi.Count / 2] - lo[lo.Count / 2] : double.NaN;
        res.Flat[e] = flat;
        res.Plumb[e] = plumb;

        Vector2d mid = 0.5 * (a + b);
        if (res.Wall[e] != -1) res.Dev[e] = Math.Abs((mid.X - mean.X) * nn2.X + (mid.Y - mean.Y) * nn2.Y);
        res.Info[e] += string.Format(", fitted: points={0}, plumb={1:F3}, flatness={2:F3}", nn, plumb, flat);
      }

      // Corners from neighbouring lines
      var outPts = new List<Vector2d>();
      if (open) outPts.Add(Project(lines[0], v[0]));   // open start: onto the first line
      for (int i = open ? 1 : 0; i < (open ? m - 1 : m); i++)
      {
        Ln l0 = lines[(i - 1 + mE) % mE], l1 = lines[i];
        Vector2d c = v[i];
        double cr = l0.D.X * l1.D.Y - l0.D.Y * l1.D.X;
        if (Math.Abs(cr) > 0.02)
        {
          Vector2d w0 = l1.O - l0.O;
          double t = (w0.X * l1.D.Y - w0.Y * l1.D.X) / cr;
          outPts.Add(l0.O + t * l0.D);
        }
        else
        {
          // Parallel neighbours: offset with connecting piece
          Vector2d p0 = Project(l0, c), p1 = Project(l1, c);
          outPts.Add(p0);
          if ((p1 - p0).Length > tol) outPts.Add(p1);
        }
      }

      if (open) outPts.Add(Project(lines[mE - 1], v[m - 1]));   // open end: onto the last line

      double z = double.IsNaN(zF) ? input[0].Z : zF;
      var poly = new Polyline(outPts.Count + 1);
      foreach (var p in outPts) poly.Add(p.X, p.Y, z);
      if (!open) poly.Add(poly[0]);
      res.Polygon = poly;

      if (open && !double.IsNaN(zF) && !double.IsNaN(zC) && zC > zF)
      {
        // one vertical rectangle per edge, then join
        var faces = new List<Brep>();
        for (int i = 0; i < poly.Count - 1; i++)
        {
          var p0 = poly[i]; var p1 = poly[i + 1];
          if (p0.DistanceTo(p1) < tol) continue;
          var f = Brep.CreateFromCornerPoints(new Point3d(p0.X, p0.Y, zF), new Point3d(p1.X, p1.Y, zF),
            new Point3d(p1.X, p1.Y, zC), new Point3d(p0.X, p0.Y, zC), tol);
          if (f != null) faces.Add(f);
        }
        var joined = Brep.JoinBreps(faces, tol);
        if (joined != null && joined.Length > 0) res.Room = joined[0];
      }
      else if (!open && !double.IsNaN(zF) && !double.IsNaN(zC) && zC > zF)
      {
        var srf = Surface.CreateExtrusion(poly.ToNurbsCurve(), new Vector3d(0, 0, zC - zF));
        if (srf != null)
        {
          var b = Brep.CreateFromSurface(srf);
          if (b != null)
          {
            res.Room = b.CapPlanarHoles(tol) ?? b;
            if (res.Room.SolidOrientation == BrepSolidOrientation.Inward) res.Room.Flip();
          }
        }
      }
      else if (!open) res.Info.Add("Floor or ceiling missing, no room volume");
      res.Info.AddRange(notes);

      return res;
    }

    // Local line: offset from the peak of a distance histogram, direction by principal component analysis
    // in a narrow band around this peak (sketch direction if the analysis is outside angTol)
    private static bool FitLocal(List<Vector2d> pts, Vector2d a, Vector2d de, double len,
      double dTol, double sinT, out Ln fit, out int count)
    {
      fit = new Ln(); count = 0;
      if (pts.Count == 0 || len <= 0 || dTol <= 0) return false;
      var nrm = new Vector2d(-de.Y, de.X);

      // Points in the band around the edge, corners excluded
      var offs = new List<double>(); var cand = new List<Vector2d>();
      foreach (var p in pts)
      {
        double t = (p.X - a.X) * de.X + (p.Y - a.Y) * de.Y;
        if (t < 0.1 * len || t > 0.9 * len) continue;
        double off = (p.X - a.X) * nrm.X + (p.Y - a.Y) * nrm.Y;
        if (Math.Abs(off) > dTol) continue;
        offs.Add(off); cand.Add(p);
      }
      if (cand.Count < 10) return false;

      // Histogram of distances, slightly smoothed
      double bw = 0.005;
      int nb = (int)Math.Ceiling(2 * dTol / bw) + 1;
      var h = new double[nb];
      foreach (var off in offs) h[Math.Min(nb - 1, (int)((off + dTol) / bw))] += 1;
      int pk = 0; double pv = -1;
      for (int i = 0; i < nb; i++)
      {
        double sv = h[i] + 0.5 * ((i > 0 ? h[i - 1] : 0) + (i < nb - 1 ? h[i + 1] : 0));
        if (sv > pv) { pv = sv; pk = i; }
      }
      double off0 = -dTol + (pk + 0.5) * bw;

      // Narrow band around the peak
      double half = Math.Max(0.015, Math.Min(0.03, 0.25 * dTol));
      var sel = new List<Vector2d>();
      double sx = 0, sy = 0;
      for (int i = 0; i < cand.Count; i++)
        if (Math.Abs(offs[i] - off0) <= half) { sel.Add(cand[i]); sx += cand[i].X; sy += cand[i].Y; }
      if (sel.Count < 10) return false;
      var mean = new Vector2d(sx / sel.Count, sy / sel.Count);

      double cxx = 0, cxy = 0, cyy = 0;
      foreach (var p in sel)
      {
        double dx = p.X - mean.X, dy = p.Y - mean.Y;
        cxx += dx * dx; cxy += dx * dy; cyy += dy * dy;
      }
      double ang = 0.5 * Math.Atan2(2 * cxy, cxx - cyy);
      var dir = new Vector2d(Math.Cos(ang), Math.Sin(ang));
      if (dir.X * de.X + dir.Y * de.Y < 0) dir = -dir;
      if (Math.Abs(dir.X * de.Y - dir.Y * de.X) > sinT) dir = de;

      fit = new Ln { O = mean, D = dir };
      count = sel.Count;
      return true;
    }

    // Point in polygon (ray casting)
    private static bool Inside(List<Vector2d> poly, double x, double y)
    {
      bool c = false;
      for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
      {
        if (((poly[i].Y > y) != (poly[j].Y > y)) &&
            (x < (poly[j].X - poly[i].X) * (y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X))
          c = !c;
      }
      return c;
    }

    private static Vector2d Project(Ln l, Vector2d p)
    {
      double t = (p.X - l.O.X) * l.D.X + (p.Y - l.O.Y) * l.D.Y;
      return l.O + t * l.D;
    }

    // Share of [t0, t1] covered by points (gaps > 5 % of the edge length count as empty)
    private static double Support(double[] T, double t0, double t1, double len)
    {
      if (len <= 0 || T.Length == 0) return 0;
      double gap = 0.05 * len;
      int i = Array.BinarySearch(T, t0);
      if (i < 0) i = ~i;
      double covered = 0, prev = t0;
      bool first = true;
      for (; i < T.Length && T[i] <= t1; i++)
      {
        double step = T[i] - prev;
        if (!first && step <= gap) covered += step;
        prev = T[i];
        first = false;
      }
      return covered / len;
    }
  }
}
