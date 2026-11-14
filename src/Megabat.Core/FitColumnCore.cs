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
  public static class FitColumnCore
  {
    public class Result
    {
      public double Cx, Cy, R, Z0, Z1, Rms, Coverage;
      public int Count;
      public bool[] Mask;
    }

    public static Result Run(Point3d[] pts, double tol, int iter, int seed)
    {
      int n = pts.Length;

      // Sample for scoring the hypotheses
      int m = Math.Min(n, 20000);
      var rnd = new Random(seed);
      var smp = new int[m];
      if (m == n) for (int i = 0; i < n; i++) smp[i] = i;
      else for (int i = 0; i < m; i++) smp[i] = rnd.Next(n);

      // Draw triples beforehand: deterministic despite parallelisation
      var tri = new int[iter, 3];
      for (int k = 0; k < iter; k++) { tri[k, 0] = rnd.Next(n); tri[k, 1] = rnd.Next(n); tri[k, 2] = rnd.Next(n); }

      var score = new int[iter];
      var hyp = new double[iter, 3];
      Parallel.For(0, iter, k =>
      {
        double cx, cy, r;
        if (!Circle3(pts[tri[k, 0]], pts[tri[k, 1]], pts[tri[k, 2]], out cx, out cy, out r)) return;
        int s = 0;
        for (int j = 0; j < m; j++)
        {
          var p = pts[smp[j]];
          double d = Math.Abs(Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy)) - r);
          if (d <= tol) s++;
        }
        score[k] = s; hyp[k, 0] = cx; hyp[k, 1] = cy; hyp[k, 2] = r;
      });

      int best = -1;
      for (int k = 0; k < iter; k++) if (score[k] > 0 && (best < 0 || score[k] > score[best])) best = k;
      if (best < 0) return null;

      double Cx = hyp[best, 0], Cy = hyp[best, 1], R = hyp[best, 2];
      bool[] mask = null;

      // Alternate between inlier selection and geometric fit
      for (int round = 0; round < 3; round++)
      {
        mask = Mask(pts, Cx, Cy, R, tol);
        var xs = new List<double>(); var ys = new List<double>();
        for (int i = 0; i < n; i++) if (mask[i]) { xs.Add(pts[i].X); ys.Add(pts[i].Y); }
        if (xs.Count < 3) return null;
        Kasa(xs, ys, out Cx, out Cy, out R);
        Geometric(xs, ys, ref Cx, ref Cy, out R);
      }
      mask = Mask(pts, Cx, Cy, R, tol);

      var res = new Result { Cx = Cx, Cy = Cy, R = R, Mask = mask };
      var zs = new List<double>();
      var bins = new int[72];
      double ss = 0;
      for (int i = 0; i < n; i++)
      {
        if (!mask[i]) continue;
        double dx = pts[i].X - Cx, dy = pts[i].Y - Cy;
        double d = Math.Sqrt(dx * dx + dy * dy) - R;
        ss += d * d;
        zs.Add(pts[i].Z);
        double a = Math.Atan2(dy, dx) + Math.PI;
        bins[Math.Min(71, (int)(a / (2 * Math.PI) * 72))]++;
      }
      res.Count = zs.Count;
      res.Rms = Math.Sqrt(ss / Math.Max(1, zs.Count));
      // A sector counts from 10 % of the mean occupancy, isolated stray points do not
      double minBin = Math.Max(3, 0.1 * zs.Count / 72.0);
      res.Coverage = bins.Count(b => b >= minBin) * 5.0;
      zs.Sort();
      // 0.5 % and 99.5 % quantiles against single outliers
      res.Z0 = zs[(int)(0.005 * (zs.Count - 1))];
      res.Z1 = zs[(int)(0.995 * (zs.Count - 1))];
      return res;
    }

    static bool[] Mask(Point3d[] pts, double cx, double cy, double r, double tol)
    {
      var mask = new bool[pts.Length];
      Parallel.For(0, pts.Length, i =>
      {
        double dx = pts[i].X - cx, dy = pts[i].Y - cy;
        mask[i] = Math.Abs(Math.Sqrt(dx * dx + dy * dy) - r) <= tol;
      });
      return mask;
    }

    // Circumcircle of three points in XY
    static bool Circle3(Point3d a, Point3d b, Point3d c, out double cx, out double cy, out double r)
    {
      cx = cy = r = 0;
      double d = 2 * (a.X * (b.Y - c.Y) + b.X * (c.Y - a.Y) + c.X * (a.Y - b.Y));
      if (Math.Abs(d) < 1e-12) return false;
      double a2 = a.X * a.X + a.Y * a.Y, b2 = b.X * b.X + b.Y * b.Y, c2 = c.X * c.X + c.Y * c.Y;
      cx = (a2 * (b.Y - c.Y) + b2 * (c.Y - a.Y) + c2 * (a.Y - b.Y)) / d;
      cy = (a2 * (c.X - b.X) + b2 * (a.X - c.X) + c2 * (b.X - a.X)) / d;
      r = Math.Sqrt((a.X - cx) * (a.X - cx) + (a.Y - cy) * (a.Y - cy));
      return r < 1e6;
    }

    // Algebraic circle fit (Kåsa), centred for numerical stability
    static void Kasa(List<double> xs, List<double> ys, out double cx, out double cy, out double r)
    {
      int n = xs.Count;
      double mx = xs.Average(), my = ys.Average();
      double suu = 0, svv = 0, suv = 0, suuu = 0, svvv = 0, suvv = 0, svuu = 0;
      for (int i = 0; i < n; i++)
      {
        double u = xs[i] - mx, v = ys[i] - my;
        suu += u * u; svv += v * v; suv += u * v;
        suuu += u * u * u; svvv += v * v * v; suvv += u * v * v; svuu += v * u * u;
      }
      double b1 = 0.5 * (suuu + suvv), b2 = 0.5 * (svvv + svuu);
      double det = suu * svv - suv * suv;
      double uc = 0, vc = 0;
      if (Math.Abs(det) > 1e-15) { uc = (b1 * svv - b2 * suv) / det; vc = (suu * b2 - suv * b1) / det; }
      cx = uc + mx; cy = vc + my;
      r = Math.Sqrt(uc * uc + vc * vc + (suu + svv) / n);
    }

    // Geometric fit: fixed-point iteration (Landau), minimises sum (|p - c| - r)²
    static void Geometric(List<double> xs, List<double> ys, ref double cx, ref double cy, out double r)
    {
      int n = xs.Count;
      double mx = xs.Average(), my = ys.Average();
      r = 0;
      for (int it = 0; it < 200; it++)
      {
        double sd = 0, sux = 0, suy = 0;
        for (int i = 0; i < n; i++)
        {
          double dx = cx - xs[i], dy = cy - ys[i];
          double d = Math.Sqrt(dx * dx + dy * dy);
          if (d < 1e-12) continue;
          sd += d; sux += dx / d; suy += dy / d;
        }
        r = sd / n;
        double nx = mx + r * sux / n, ny = my + r * suy / n;
        double step = Math.Abs(nx - cx) + Math.Abs(ny - cy);
        cx = nx; cy = ny;
        if (step < 1e-9) break;
      }
    }

    public static PointCloud Subset(PointCloud cloud, bool[] mask)
    {
      var pts = cloud.GetPoints();
      var cols = cloud.ContainsColors ? cloud.GetColors() : null;
      var res = new PointCloud();
      for (int i = 0; i < pts.Length; i++)
      {
        if (!mask[i]) continue;
        if (cols != null) res.Add(pts[i], cols[i]); else res.Add(pts[i]);
      }
      return res;
    }
  }
}
