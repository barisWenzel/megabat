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
  public static class SplitSlicesCore
  {
    public class Result { public List<PointCloud> Clouds = new List<PointCloud>(); public List<Plane> Planes; }

    const int Block = 4000000;

    public static Result Run(PointCloud cloud, List<Plane> planes, double overlap, out string warn)
    {
      warn = null;
      var res = new Result();
      Vector3d nv = planes[0].Normal; nv.Unitize();
      double nx = nv.X, ny = nv.Y, nz = nv.Z;

      // Sort planes as distances along n; check parallelism
      int bad = 0;
      var ofs = new List<(double o, Plane p)>();
      foreach (var p in planes)
      {
        var v = p.Normal; v.Unitize();
        if (Math.Abs(v * nv) < Math.Cos(Rhino.RhinoMath.ToRadians(1))) bad++;
        ofs.Add((nx * p.OriginX + ny * p.OriginY + nz * p.OriginZ, p));
      }
      if (bad > 0) warn = bad + " plane(s) not parallel to the first; slicing still follows its normal";
      ofs.Sort((a, b) => a.o.CompareTo(b.o));
      var o = ofs.Select(x => x.o).ToArray();
      res.Planes = ofs.Select(x => x.p).ToList();
      int K = o.Length, S = K + 1;
      for (int s = 0; s < S; s++) res.Clouds.Add(new PointCloud());

      int n = cloud.Count;
      bool hasCol = cloud.ContainsColors, hasNrm = cloud.ContainsNormals;
      int bl = Math.Min(Block, n);
      var pts = new Point3d[bl];
      var cols = hasCol ? new Color[bl] : null;
      var nrms = hasNrm ? new Vector3d[bl] : null;
      var prim = new int[bl];    // slice
      var sec = new int[bl];     // second slice in the overlap band, otherwise -1

      for (int start = 0; start < n; start += Block)
      {
        int len = Math.Min(Block, n - start);

        Parallel.For(0, len, i =>
        {
          var it = cloud[start + i];
          var p = it.Location;
          pts[i] = p;
          if (hasCol) cols[i] = it.Color;
          if (hasNrm) nrms[i] = it.Normal;
          double d = nx * p.X + ny * p.Y + nz * p.Z;
          // binary search: first plane above d
          int lo = 0, hi = K;
          while (lo < hi) { int mid = (lo + hi) >> 1; if (o[mid] <= d) lo = mid + 1; else hi = mid; }
          int s = lo;
          prim[i] = s;
          sec[i] = -1;
          if (overlap > 0)
          {
            if (s > 0 && d - o[s - 1] < overlap) sec[i] = s - 1;
            else if (s < K && o[s] - d < overlap) sec[i] = s + 1;
          }
        });

        // Count, then one AddRange per slice
        var cnt = new int[S];
        for (int i = 0; i < len; i++) { cnt[prim[i]]++; if (sec[i] >= 0) cnt[sec[i]]++; }
        var bp = new Point3d[S][];
        var bc = new Color[S][];
        var bn = new Vector3d[S][];
        for (int s = 0; s < S; s++)
        {
          bp[s] = new Point3d[cnt[s]];
          if (hasCol) bc[s] = new Color[cnt[s]];
          if (hasNrm) bn[s] = new Vector3d[cnt[s]];
        }
        var k = new int[S];
        for (int i = 0; i < len; i++)
        {
          Put(prim[i], i, k, bp, bc, bn, pts, cols, nrms);
          if (sec[i] >= 0) Put(sec[i], i, k, bp, bc, bn, pts, cols, nrms);
        }
        for (int s = 0; s < S; s++)
        {
          if (cnt[s] == 0) continue;
          if (hasCol && hasNrm) res.Clouds[s].AddRange(bp[s], bn[s], bc[s]);
          else if (hasCol) res.Clouds[s].AddRange(bp[s], bc[s]);
          else if (hasNrm) res.Clouds[s].AddRange(bp[s], bn[s]);
          else res.Clouds[s].AddRange(bp[s]);
        }
      }
      return res;
    }

    static void Put(int s, int i, int[] k, Point3d[][] bp, Color[][] bc, Vector3d[][] bn,
      Point3d[] pts, Color[] cols, Vector3d[] nrms)
    {
      int j = k[s]++;
      bp[s][j] = pts[i];
      if (cols != null) bc[s][j] = cols[i];
      if (nrms != null) bn[s][j] = nrms[i];
    }
  }
}
