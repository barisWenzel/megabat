// CloudUtil: helpers for large point clouds
using System;
using System.Drawing;
using System.Threading.Tasks;
using Rhino.Geometry;

namespace Megabat.Core
{
  public static class CloudUtil
  {
    // Every k-th point, at most max points, colours and normals are kept
    public static PointCloud Thin(PointCloud c, int max)
    {
      if (c == null) return null;
      int n = c.Count;
      if (n <= max || max <= 0) return c;
      int k = (n + max - 1) / max;
      int m = (n + k - 1) / k;
      bool hasCol = c.ContainsColors, hasNrm = c.ContainsNormals;
      var pts = new Point3d[m];
      var cols = hasCol ? new Color[m] : null;
      var nrm = hasNrm ? new Vector3d[m] : null;
      Parallel.For(0, m, j =>
      {
        var it = c[j * k];
        pts[j] = it.Location;
        if (hasCol) cols[j] = it.Color;
        if (hasNrm) nrm[j] = it.Normal;
      });
      return Make(pts, cols, nrm);
    }

    // Several clouds into one new cloud. Colours are kept if any cloud has them (others get grey),
    // normals only if every cloud has them.
    public static PointCloud Merge(System.Collections.Generic.IList<PointCloud> clouds)
    {
      long total = 0;
      bool anyCol = false, allNrm = clouds.Count > 0;
      foreach (var c in clouds) { total += c.Count; anyCol |= c.ContainsColors; allNrm &= c.ContainsNormals; }
      if (total > int.MaxValue) throw new InvalidOperationException("Too many points for one cloud");
      int n = (int)total;
      var pts = new Point3d[n];
      var cols = anyCol ? new Color[n] : null;
      var nrm = allNrm ? new Vector3d[n] : null;
      int off = 0;
      foreach (var c in clouds)
      {
        int o = off, m = c.Count;
        bool hc = c.ContainsColors;
        Parallel.For(0, m, i =>
        {
          var it = c[i];
          pts[o + i] = it.Location;
          if (cols != null) cols[o + i] = hc ? it.Color : Color.Gray;
          if (nrm != null) nrm[o + i] = it.Normal;
        });
        off += m;
      }
      return Make(pts, cols, nrm);
    }

    public static PointCloud Make(Point3d[] pts, Color[] cols, Vector3d[] nrm)
    {
      var res = new PointCloud();
      if (pts.Length == 0) return res;
      if (cols != null && nrm != null) res.AddRange(pts, nrm, cols);
      else if (cols != null) res.AddRange(pts, cols);
      else if (nrm != null) res.AddRange(pts, nrm);
      else res.AddRange(pts);
      return res;
    }
  }
}
