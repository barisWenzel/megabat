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
