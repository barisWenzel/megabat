// View planning: standpoints that see as much of the surfaces as possible (scanner or camera positions)
// 1 occupancy grid with a normal per voxel, 2 candidate grid at a height above the floor, 3 visible voxels per candidate
// (line of sight through the grid, distance and incidence angle), 4 greedy set cover
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Rhino.Geometry;

namespace Megabat.Core
{
  public static class ViewCore
  {
    public class Options
    {
      public double Voxel = 0.1, Height = 1.5, Step = 0.5, Clear = 0.3;
      public double DMin = 0.5, DMax = 15, Angle = 70, Cover = 0.95;
      public int Max = 5;
      public bool Room = true;   // candidates only below a ceiling
    }

    public class Result
    {
      public List<Point3d> Points = new List<Point3d>();      // chosen standpoints in order
      public List<double> Cover = new List<double>();        // cumulative share of the surface voxels seen
      public List<int> Gain = new List<int>();               // new voxels per standpoint
      public List<List<Point3d>> Seen = new List<List<Point3d>>();   // voxel centres first seen from each standpoint
      public List<Point3d> Candidates = new List<Point3d>();
      public int Voxels;                                       // occupied voxels
    }

    public static Result Run(PointCloud c, Options o)
    {
      var r = new Result();
      if (c == null || c.Count == 0) return r;
      double v = o.Voxel;
      var bb = c.GetBoundingBox(true);
      var org = bb.Min - new Vector3d(v, v, v);
      int nx = (int)Math.Ceiling((bb.Max.X - org.X) / v) + 2, ny = (int)Math.Ceiling((bb.Max.Y - org.Y) / v) + 2, nz = (int)Math.Ceiling((bb.Max.Z - org.Z) / v) + 2;
      long cells = (long)nx * ny * nz;
      if (cells > 60_000_000) throw new ArgumentException("grid too large: increase voxel");
      int Idx(int i, int j, int k) => (k * ny + j) * nx + i;

      // 1 occupancy and covariance per voxel
      var sums = new Dictionary<int, double[]>();   // n, sx, sy, sz, sxx, sxy, sxz, syy, syz, szz
      foreach (var it in c)
      {
        var p = it.Location;
        int i = (int)((p.X - org.X) / v), j = (int)((p.Y - org.Y) / v), k = (int)((p.Z - org.Z) / v);
        int id = Idx(i, j, k);
        if (!sums.TryGetValue(id, out var s)) sums[id] = s = new double[10];
        double x = p.X - org.X, y = p.Y - org.Y, z = p.Z - org.Z;
        s[0]++; s[1] += x; s[2] += y; s[3] += z; s[4] += x * x; s[5] += x * y; s[6] += x * z; s[7] += y * y; s[8] += y * z; s[9] += z * z;
      }
      var occ = new bool[cells];
      var vox = new List<int>(); var nrm = new List<Vector3d>(); var cen = new List<Point3d>();
      foreach (var kv in sums)
      {
        if (kv.Value[0] < 3) continue;   // single stray points do not block or count
        occ[kv.Key] = true;
        var s = kv.Value; double m = s[0];
        double mx = s[1] / m, my = s[2] / m, mz = s[3] / m;
        var C = new double[3, 3]
        {
          { s[4] / m - mx * mx, s[5] / m - mx * my, s[6] / m - mx * mz },
          { s[5] / m - mx * my, s[7] / m - my * my, s[8] / m - my * mz },
          { s[6] / m - mx * mz, s[8] / m - my * mz, s[9] / m - mz * mz }
        };
        vox.Add(kv.Key); nrm.Add(Smallest(C)); cen.Add(new Point3d(org.X + mx, org.Y + my, org.Z + mz));
      }
      r.Voxels = vox.Count;
      int nv = vox.Count;

      // 2 candidates: per column the floor (lowest occupied voxel), standpoint at floor + height, free around, ceiling above
      int clr = Math.Max(1, (int)Math.Ceiling(o.Clear / v));
      int st = Math.Max(1, (int)Math.Round(o.Step / v));
      var cand = new List<Point3d>();
      for (int j = 1; j < ny - 1; j += st)
        for (int i = 1; i < nx - 1; i += st)
        {
          int kf = -1;
          for (int k = 0; k < nz; k++) if (occ[Idx(i, j, k)]) { kf = k; break; }
          if (kf < 0) continue;
          double z = org.Z + (kf + 0.5) * v + o.Height;
          int kc = (int)((z - org.Z) / v);
          if (kc >= nz - 1) continue;
          bool free = true;
          for (int dk = -clr; dk <= clr && free; dk++)
            for (int dj = -clr; dj <= clr && free; dj++)
              for (int di = -clr; di <= clr && free; di++)
              {
                int a = i + di, b = j + dj, k = kc + dk;
                if (a < 0 || b < 0 || k < 0 || a >= nx || b >= ny || k >= nz) continue;
                if (occ[Idx(a, b, k)]) free = false;
              }
          if (!free) continue;
          if (o.Room)
          {
            bool roof = false;
            for (int k = kc + 1; k < nz; k++) if (occ[Idx(i, j, k)]) { roof = true; break; }
            if (!roof) continue;
          }
          cand.Add(new Point3d(org.X + (i + 0.5) * v, org.Y + (j + 0.5) * v, z));
        }
      r.Candidates = cand;
      if (cand.Count == 0) return r;

      // 3 visible voxels per candidate as bit sets
      double cosMin = Math.Cos(o.Angle * Math.PI / 180);
      int words = (nv + 63) / 64;
      var vis = new ulong[cand.Count][];
      Parallel.For(0, cand.Count, ci =>
      {
        var bits = new ulong[words]; var p0 = cand[ci];
        for (int t = 0; t < nv; t++)
        {
          var d = cen[t] - p0; double len = d.Length;
          if (len < o.DMin || len > o.DMax) continue;
          if (Math.Abs(d * nrm[t]) / len < cosMin) continue;
          if (Clear(p0, cen[t], vox[t])) bits[t >> 6] |= 1UL << (t & 63);
        }
        vis[ci] = bits;
      });

      // line of sight: 3D DDA from p0 to the target voxel; any other occupied voxel more than 1.5 voxels before the target blocks
      bool Clear(Point3d a, Point3d b, int target)
      {
        var d = b - a; double len = d.Length; d /= len;
        double stopAt = len - 1.5 * v;
        int i = (int)((a.X - org.X) / v), j = (int)((a.Y - org.Y) / v), k = (int)((a.Z - org.Z) / v);
        int sx = d.X > 0 ? 1 : -1, sy = d.Y > 0 ? 1 : -1, sz = d.Z > 0 ? 1 : -1;
        double tx = Next(a.X - org.X, d.X, i, v), ty = Next(a.Y - org.Y, d.Y, j, v), tz = Next(a.Z - org.Z, d.Z, k, v);
        double dx = Math.Abs(v / d.X), dy = Math.Abs(v / d.Y), dz = Math.Abs(v / d.Z);
        double t = 0;
        while (t < stopAt)
        {
          if (tx < ty && tx < tz) { i += sx; t = tx; tx += dx; }
          else if (ty < tz) { j += sy; t = ty; ty += dy; }
          else { k += sz; t = tz; tz += dz; }
          if (t >= stopAt) break;
          if (i < 0 || j < 0 || k < 0 || i >= nx || j >= ny || k >= nz) return false;
          int id = Idx(i, j, k);
          if (id == target) return true;
          if (occ[id]) return false;
        }
        return true;
      }

      // 4 greedy set cover
      var covered = new ulong[words]; int total = 0;
      var used = new bool[cand.Count];
      for (int n = 0; n < o.Max; n++)
      {
        int best = -1, gain = 0;
        for (int ci = 0; ci < cand.Count; ci++)
        {
          if (used[ci]) continue;
          int g = 0; var b = vis[ci];
          for (int w = 0; w < words; w++) g += System.Numerics.BitOperations.PopCount(b[w] & ~covered[w]);
          if (g > gain) { gain = g; best = ci; }
        }
        if (best < 0 || gain == 0) break;
        used[best] = true;
        var seen = new List<Point3d>();
        for (int w = 0; w < words; w++)
        {
          ulong nb = vis[best][w] & ~covered[w];
          while (nb != 0) { int bit = System.Numerics.BitOperations.TrailingZeroCount(nb); seen.Add(cen[w * 64 + bit]); nb &= nb - 1; }
          covered[w] |= vis[best][w];
        }
        total += gain;
        r.Points.Add(cand[best]); r.Gain.Add(gain); r.Cover.Add((double)total / nv); r.Seen.Add(seen);
        if ((double)total / nv >= o.Cover) break;
      }
      return r;
    }

    static double Next(double x, double d, int i, double v)
    {
      if (Math.Abs(d) < 1e-12) return double.MaxValue;
      double edge = d > 0 ? (i + 1) * v : i * v;
      return (edge - x) / d;
    }

    // eigenvector of the smallest eigenvalue of a symmetric 3x3 matrix (Jacobi)
    static Vector3d Smallest(double[,] a)
    {
      var A = (double[,])a.Clone(); var V = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
      for (int sweep = 0; sweep < 30; sweep++)
      {
        double off = A[0, 1] * A[0, 1] + A[0, 2] * A[0, 2] + A[1, 2] * A[1, 2];
        if (off < 1e-30) break;
        for (int p = 0; p < 2; p++)
          for (int q = p + 1; q < 3; q++)
          {
            if (Math.Abs(A[p, q]) < 1e-300) continue;
            double th = (A[q, q] - A[p, p]) / (2 * A[p, q]);
            double t = (th >= 0 ? 1 : -1) / (Math.Abs(th) + Math.Sqrt(th * th + 1));
            double cs = 1 / Math.Sqrt(t * t + 1), sn = t * cs;
            for (int k = 0; k < 3; k++) { double akp = A[k, p], akq = A[k, q]; A[k, p] = cs * akp - sn * akq; A[k, q] = sn * akp + cs * akq; }
            for (int k = 0; k < 3; k++) { double apk = A[p, k], aqk = A[q, k]; A[p, k] = cs * apk - sn * aqk; A[q, k] = sn * apk + cs * aqk; }
            for (int k = 0; k < 3; k++) { double vkp = V[k, p], vkq = V[k, q]; V[k, p] = cs * vkp - sn * vkq; V[k, q] = sn * vkp + cs * vkq; }
          }
      }
      int m = 0; for (int i = 1; i < 3; i++) if (A[i, i] < A[m, m]) m = i;
      var n = new Vector3d(V[0, m], V[1, m], V[2, m]); n.Unitize(); return n;
    }
  }
}
