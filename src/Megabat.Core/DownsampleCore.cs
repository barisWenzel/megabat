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
  public static class DownsampleCore
  {
    const int Block = 1 << 20;

    // Sums per cell
    struct Acc { public double X, Y, Z, R, G, B, NX, NY, NZ; public int M; }

    static long Key(Point3d p, Point3d o, double inv)
    {
      return ((long)((p.X - o.X) * inv) << 42) | ((long)((p.Y - o.Y) * inv) << 21) | (long)((p.Z - o.Z) * inv);
    }

    public static PointCloud Run(PointCloud cloud, double voxel)
    {
      int n = cloud.Count;
      bool hasCol = cloud.ContainsColors, hasNrm = cloud.ContainsNormals;
      Point3d o = cloud.GetBoundingBox(true).Min;
      double inv = 1.0 / voxel;
      int nBlocks = (n + Block - 1) / Block;

      // Read blocks in parallel, one dictionary per thread, merged at the end
      var all = new Dictionary<long, Acc>();
      var gate = new object();
      Parallel.For(0, nBlocks,
        () => new Dictionary<long, Acc>(),
        (blk, state, loc) =>
        {
          int start = blk * Block, end = Math.Min(n, start + Block);
          for (int i = start; i < end; i++)
          {
            var it = cloud[i];
            var p = it.Location;
            ref var a = ref CollectionsMarshal.GetValueRefOrAddDefault(loc, Key(p, o, inv), out _);
            a.X += p.X; a.Y += p.Y; a.Z += p.Z; a.M++;
            if (hasCol) { var c = it.Color; a.R += c.R; a.G += c.G; a.B += c.B; }
            if (hasNrm) { var v = it.Normal; a.NX += v.X; a.NY += v.Y; a.NZ += v.Z; }
          }
          return loc;
        },
        loc =>
        {
          lock (gate)
            foreach (var kv in loc)
            {
              ref var a = ref CollectionsMarshal.GetValueRefOrAddDefault(all, kv.Key, out _);
              var b = kv.Value;
              a.X += b.X; a.Y += b.Y; a.Z += b.Z; a.R += b.R; a.G += b.G; a.B += b.B;
              a.NX += b.NX; a.NY += b.NY; a.NZ += b.NZ; a.M += b.M;
            }
        });

      // Compute means and append in one go
      int m = all.Count;
      var pts = new Point3d[m];
      var cols = hasCol ? new Color[m] : null;
      var nrm = hasNrm ? new Vector3d[m] : null;
      int k = 0;
      foreach (var a in all.Values)
      {
        double d = 1.0 / a.M;
        pts[k] = new Point3d(a.X * d, a.Y * d, a.Z * d);
        if (hasCol) cols[k] = Color.FromArgb((int)(a.R * d), (int)(a.G * d), (int)(a.B * d));
        if (hasNrm) { var v = new Vector3d(a.NX, a.NY, a.NZ); v.Unitize(); nrm[k] = v; }
        k++;
      }
      return Make(pts, cols, nrm);
    }

    // Number of occupied cells for a given voxel size (count only)
    static int CountCells(PointCloud cloud, double voxel, Point3d o)
    {
      int n = cloud.Count, nBlocks = (n + Block - 1) / Block;
      double inv = 1.0 / voxel;
      var all = new HashSet<long>();
      var gate = new object();
      Parallel.For(0, nBlocks,
        () => new HashSet<long>(),
        (blk, state, loc) =>
        {
          int start = blk * Block, end = Math.Min(n, start + Block);
          for (int i = start; i < end; i++) loc.Add(Key(cloud[i].Location, o, inv));
          return loc;
        },
        loc => { lock (gate) all.UnionWith(loc); });
      return all.Count;
    }

    // Voxel size for about N points: start from the box surface area, then secant steps on a log scale
    public static double FindVoxel(PointCloud cloud, int target)
    {
      var bb = cloud.GetBoundingBox(true);
      var d = bb.Diagonal;
      double area = 2 * (d.X * d.Y + d.Y * d.Z + d.X * d.Z);
      double v1 = Math.Max(1e-4, Math.Sqrt(Math.Max(area, 1e-6) / target));
      int c1 = CountCells(cloud, v1, bb.Min);
      double v0 = 0; int c0 = 0;
      for (int it = 0; it < 8; it++)
      {
        if (Math.Abs(c1 - target) <= 0.05 * target) break;
        double slope = -2;   // point clouds are mostly surfaces: cell count ~ 1/v²
        if (c0 > 0 && v0 != v1 && c0 != c1) slope = Math.Log((double)c1 / c0) / Math.Log(v1 / v0);
        if (slope > -0.5 || slope < -3.5) slope = -2;
        double vn = v1 * Math.Pow((double)target / c1, 1.0 / slope);
        vn = Math.Max(v1 / 4, Math.Min(v1 * 4, vn));
        v0 = v1; c0 = c1; v1 = vn; c1 = CountCells(cloud, v1, bb.Min);
      }
      return v1;
    }

    // Exactly N points, evenly spread over the indices (every k-th)
    public static PointCloud Stride(PointCloud cloud, int target)
    {
      long n = cloud.Count;
      bool hasCol = cloud.ContainsColors, hasNrm = cloud.ContainsNormals;
      var pts = new Point3d[target];
      var cols = hasCol ? new Color[target] : null;
      var nrm = hasNrm ? new Vector3d[target] : null;
      Parallel.For(0, target, i =>
      {
        var it = cloud[(int)(i * n / target)];
        pts[i] = it.Location;
        if (hasCol) cols[i] = it.Color;
        if (hasNrm) nrm[i] = it.Normal;
      });
      return Make(pts, cols, nrm);
    }

    static PointCloud Make(Point3d[] pts, Color[] cols, Vector3d[] nrm)
    {
      var res = new PointCloud();
      if (cols != null && nrm != null) res.AddRange(pts, nrm, cols);
      else if (cols != null) res.AddRange(pts, cols);
      else if (nrm != null) res.AddRange(pts, nrm);
      else res.AddRange(pts);
      return res;
    }
  }
}
