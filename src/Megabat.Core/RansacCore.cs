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
  public static class RansacCore
  {
    public class Result
    {
      public List<Plane> Planes = new List<Plane>();
      public List<PointCloud> Segments = new List<PointCloud>();
      public PointCloud Rest = new PointCloud();
    }

    private class Local { public int S = -1, I = int.MaxValue; public double A, B, C, D; }

    public static readonly Color[] Palette = {
      Color.FromArgb(220,20,60),   Color.FromArgb(50,205,50),   Color.FromArgb(255,215,0),
      Color.FromArgb(65,105,225),  Color.FromArgb(255,127,80),  Color.FromArgb(153,50,204),
      Color.FromArgb(64,224,208),  Color.FromArgb(255,0,255),   Color.FromArgb(173,255,47),
      Color.FromArgb(255,192,203), Color.FromArgb(0,128,128),   Color.FromArgb(230,230,250),
      Color.FromArgb(160,82,45),   Color.FromArgb(255,250,205), Color.FromArgb(128,0,0),
      Color.FromArgb(127,255,212), Color.FromArgb(128,128,0),   Color.FromArgb(255,218,185),
      Color.FromArgb(0,0,128),     Color.FromArgb(128,128,128), Color.FromArgb(255,255,255),
      Color.FromArgb(0,0,0) };

    public static Result Run(PointCloud cloud, double tol, int iter, int minPts, int maxPlanes, int sample, int seed)
    {
      var res = new Result();
      int N = cloud.Count;
      var pts = cloud.GetPoints();
      var cols = cloud.ContainsColors ? cloud.GetColors() : null;
      var X = new double[N]; var Y = new double[N]; var Z = new double[N];
      for (int i = 0; i < N; i++) { X[i] = pts[i].X; Y[i] = pts[i].Y; Z[i] = pts[i].Z; }

      var act = new int[N];
      for (int i = 0; i < N; i++) act[i] = i;
      int m = N;

      var rnd = new Random(seed);
      var order = new int[Palette.Length];
      for (int i = 0; i < order.Length; i++) order[i] = i;
      for (int i = order.Length - 1; i > 0; i--) { int r = rnd.Next(i + 1); int t = order[i]; order[i] = order[r]; order[r] = t; }

      for (int k = 0; k < maxPlanes && m >= minPts; k++)
      {
        // Score hypotheses on a sample only
        int s = Math.Min(Math.Max(3, sample), m);
        var sub = new int[s];
        for (int j = 0; j < s; j++) sub[j] = act[rnd.Next(m)];

        // Draw triples sequentially beforehand: result independent of thread order
        var ta = new int[iter]; var tb = new int[iter]; var tc = new int[iter];
        for (int i = 0; i < iter; i++) { ta[i] = sub[rnd.Next(s)]; tb[i] = sub[rnd.Next(s)]; tc[i] = sub[rnd.Next(s)]; }

        int bestS = -1, bestI = int.MaxValue;
        double[] bp = null;
        var lk = new object();

        Parallel.For(0, iter,
          () => new Local(),
          (i, st, L) =>
          {
            int a = ta[i], b = tb[i], c = tc[i];
            double ux = X[b] - X[a], uy = Y[b] - Y[a], uz = Z[b] - Z[a];
            double vx = X[c] - X[a], vy = Y[c] - Y[a], vz = Z[c] - Z[a];
            double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (len < 1e-12) return L;
            nx /= len; ny /= len; nz /= len;
            double d = -(nx * X[a] + ny * Y[a] + nz * Z[a]);

            int cnt = 0;
            for (int j = 0; j < s; j++)
            {
              int q = sub[j];
              if (Math.Abs(nx * X[q] + ny * Y[q] + nz * Z[q] + d) < tol) cnt++;
            }
            if (cnt > L.S || (cnt == L.S && i < L.I)) { L.S = cnt; L.I = i; L.A = nx; L.B = ny; L.C = nz; L.D = d; }
            return L;
          },
          L => { lock (lk) if (L.S > bestS || (L.S == bestS && L.I < bestI)) { bestS = L.S; bestI = L.I; bp = new[] { L.A, L.B, L.C, L.D }; } });

        if (bp == null) break;

        // Full classification, least-squares refinement, classify again
        int nIn;
        var f = Classify(X, Y, Z, act, m, bp, tol, out nIn);
        if (nIn < minPts) break;
        var inPts = new List<Point3d>(nIn);
        for (int j = 0; j < m; j++) if (f[j]) inPts.Add(pts[act[j]]);
        Plane pl;
        if (Plane.FitPlaneToPoints(inPts, out pl) == PlaneFitResult.Success)
        {
          Vector3d n = pl.Normal;
          bp = new[] { n.X, n.Y, n.Z, -(n.X * pl.OriginX + n.Y * pl.OriginY + n.Z * pl.OriginZ) };
          f = Classify(X, Y, Z, act, m, bp, tol, out nIn);
          if (nIn < minPts) break;
        }
        else pl = new Plane(inPts[0], new Vector3d(bp[0], bp[1], bp[2]));

        // Segment with palette colour, compact active indices
        var seg = new PointCloud();
        Color col = Palette[order[k % Palette.Length]];
        int w = 0;
        for (int j = 0; j < m; j++)
        {
          int q = act[j];
          if (f[j]) seg.Add(pts[q], col);
          else act[w++] = q;
        }
        m = w;

        pl.Origin = OriginOnSegment(seg.GetPoints(), pl);
        res.Planes.Add(pl);
        res.Segments.Add(seg);
      }

      for (int j = 0; j < m; j++)
      {
        int q = act[j];
        if (cols != null) res.Rest.Add(pts[q], cols[q]); else res.Rest.Add(pts[q]);
      }
      return res;
    }

    public static bool[] Classify(double[] X, double[] Y, double[] Z, int[] act, int m, double[] p, double tol, out int cnt)
    {
      var f = new bool[m];
      int c = 0;
      Parallel.For(0, m, () => 0,
        (j, st, loc) =>
        {
          int q = act[j];
          if (Math.Abs(p[0] * X[q] + p[1] * Y[q] + p[2] * Z[q] + p[3]) < tol) { f[j] = true; loc++; }
          return loc;
        },
        loc => Interlocked.Add(ref c, loc));
      cnt = c;
      return f;
    }

    // Hull centroid, snapped to the nearest segment point
    public static Point3d OriginOnSegment(Point3d[] sp, Plane pl)
    {
      Point3d cen = HullCentroid(sp, pl);
      int bi = 0; double bd = double.MaxValue;
      for (int j = 0; j < sp.Length; j++)
      {
        double dd = sp[j].DistanceToSquared(cen);
        if (dd < bd) { bd = dd; bi = j; }
      }
      return pl.ClosestPoint(sp[bi]);
    }

    public static Point3d HullCentroid(Point3d[] p, Plane pl)
    {
      int n = p.Length;
      var uv = new (double u, double v)[n];
      for (int i = 0; i < n; i++)
      {
        Vector3d d = p[i] - pl.Origin;
        uv[i] = (d * pl.XAxis, d * pl.YAxis);
      }
      Array.Sort(uv, (a, b) => a.u != b.u ? a.u.CompareTo(b.u) : a.v.CompareTo(b.v));

      // Convex hull (monotone chain)
      var h = new (double u, double v)[2 * n];
      int k = 0;
      for (int i = 0; i < n; i++)
      {
        while (k >= 2 && Cross(h[k - 2], h[k - 1], uv[i]) <= 0) k--;
        h[k++] = uv[i];
      }
      for (int i = n - 2, t = k + 1; i >= 0; i--)
      {
        while (k >= t && Cross(h[k - 2], h[k - 1], uv[i]) <= 0) k--;
        h[k++] = uv[i];
      }
      k--;

      // Area centroid of the polygon
      double A = 0, cx = 0, cy = 0;
      for (int i = 0; i < k; i++)
      {
        var a = h[i]; var b = h[(i + 1) % k];
        double c = a.u * b.v - b.u * a.v;
        A += c; cx += (a.u + b.u) * c; cy += (a.v + b.v) * c;
      }
      if (k < 3 || Math.Abs(A) < 1e-12)
      {
        double mu = 0, mv = 0;
        foreach (var q in uv) { mu += q.u; mv += q.v; }
        return pl.PointAt(mu / n, mv / n);
      }
      return pl.PointAt(cx / (3 * A), cy / (3 * A));
    }

    private static double Cross((double u, double v) o, (double u, double v) a, (double u, double v) b)
    {
      return (a.u - o.u) * (b.v - o.v) - (a.v - o.v) * (b.u - o.u);
    }
  }
}
