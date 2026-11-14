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
  public static class CropBrepCore
  {
    public class Result
    {
      public PointCloud Inside = new PointCloud(), Rest = new PointCloud();
      public List<PointCloud> Parts = new List<PointCloud>();   // one cloud per Brep (only without merge)
      public List<int> Index = new List<int>();
      public int InCount;
      public string Warning;
    }

    const int Block = 4000000;

    // In blocks: read and test points in parallel, sub-clouds via AddRange.
    // Each point gets the index of the first Brep containing it (-1 = none).
    // merge = true: one cloud from all Breps; false: one cloud per Brep in Parts. invert implies merge.
    public static Result Run(PointCloud cloud, List<Brep> breps, double tol, bool invert, bool cut, bool wantIndex = true, bool merge = true)
    {
      var res = new Result();
      int n = cloud.Count;
      bool hasCol = cloud.ContainsColors, hasNrm = cloud.ContainsNormals;
      if (invert) merge = true;

      // Per Brep: convex planes (fast) or closed mesh; skip open Breps
      int nb = breps.Count;
      var conv = new double[nb][];
      var meshes = new Mesh[nb];
      var boxes = new BoundingBox[nb];
      var ok = new bool[nb];
      int open = 0;
      for (int k = 0; k < nb; k++)
      {
        var b = breps[k];
        if (!merge) res.Parts.Add(new PointCloud());
        if (b == null) continue;
        if (!b.IsSolid) { open++; continue; }
        var hs = ConvexPlanes(b);
        if (hs != null)
        {
          var cbb = b.GetBoundingBox(true); cbb.Inflate(tol);
          conv[k] = hs; boxes[k] = cbb; ok[k] = true;
          continue;
        }
        var parts = Mesh.CreateFromBrep(b, MeshingParameters.QualityRenderMesh);
        if (parts == null || parts.Length == 0) continue;
        var m = new Mesh();
        foreach (var p in parts) m.Append(p);
        m.Weld(Math.PI);
        m.FaceNormals.ComputeFaceNormals();
        var bb = m.GetBoundingBox(true);
        bb.Inflate(tol);
        meshes[k] = m; boxes[k] = bb; ok[k] = true;
      }
      if (open > 0) res.Warning = open + " Brep(s) not closed, skipped";

      int bl = Math.Min(Block, n);
      var pts = new Point3d[bl];
      var cols = hasCol ? new System.Drawing.Color[bl] : null;
      var nrms = hasNrm ? new Vector3d[bl] : null;
      var lab = new int[bl];

      for (int start = 0; start < n; start += Block)
      {
        int len = Math.Min(Block, n - start);
        // Mesh copies per thread, pre-filter by bounding box
        Parallel.For(0, len,
          () => { var l = new Mesh[nb]; for (int k = 0; k < nb; k++) l[k] = meshes[k]?.DuplicateMesh(); return l; },
          (i, st, local) =>
          {
            var it = cloud[start + i];
            var p = it.Location;
            pts[i] = p;
            if (hasCol) cols[i] = it.Color;
            if (hasNrm) nrms[i] = it.Normal;
            int hit = -1;
            for (int k = 0; k < nb && hit < 0; k++)
            {
              if (!ok[k] || !boxes[k].Contains(p)) continue;
              bool inside;
              var h = conv[k];
              if (h != null)
              {
                double dmax = double.MinValue;
                for (int q = 0; q < h.Length; q += 4) { double d = h[q] * p.X + h[q + 1] * p.Y + h[q + 2] * p.Z - h[q + 3]; if (d > dmax) dmax = d; }
                inside = dmax <= tol;   // inside or at most tol in front of a face
              }
              else
              {
                inside = local[k].IsPointInside(p, 1e-6, false);
                if (!inside && tol > 0) inside = local[k].ClosestPoint(p, out _, tol) >= 0;   // margin outside
              }
              if (inside) hit = k;
            }
            lab[i] = hit;
            return local;
          },
          local => { });

        for (int i = 0; i < len; i++) if ((lab[i] >= 0) != invert) { res.InCount++; if (wantIndex) res.Index.Add(start + i); }
        if (merge) Append(res.Inside, pts, cols, nrms, lab, len, l => (l >= 0) != invert);
        else for (int k = 0; k < nb; k++) { int kk = k; Append(res.Parts[k], pts, cols, nrms, lab, len, l => l == kk); }
        if (cut) Append(res.Rest, pts, cols, nrms, lab, len, l => (l >= 0) == invert);
      }
      return res;
    }

    // Planar convex Brep (e.g. box): face planes with outward normal as (nx, ny, nz, d); otherwise null
    static double[] ConvexPlanes(Brep b)
    {
      var h = new List<double>();
      var amp = AreaMassProperties.Compute(b);
      Point3d c = amp != null ? amp.Centroid : b.GetBoundingBox(true).Center;
      foreach (var f in b.Faces)
      {
        Plane pl;
        if (!f.IsPlanar(1e-6) || !f.TryGetPlane(out pl, 1e-6)) return null;
        var n = pl.Normal; n.Unitize();
        double d = n * (Vector3d)pl.Origin;
        if (n * (Vector3d)c - d > 0) { n = -n; d = -d; }   // point outwards
        h.Add(n.X); h.Add(n.Y); h.Add(n.Z); h.Add(d);
      }
      // convex if all vertices lie on or behind every plane
      double eps = 1e-6 * Math.Max(1, b.GetBoundingBox(true).Diagonal.Length);
      foreach (var v in b.Vertices)
        for (int q = 0; q < h.Count; q += 4)
          if (h[q] * v.Location.X + h[q + 1] * v.Location.Y + h[q + 2] * v.Location.Z - h[q + 3] > eps) return null;
      return h.ToArray();
    }

    // Append a subset of a block in one go
    static void Append(PointCloud target, Point3d[] pts, System.Drawing.Color[] cols, Vector3d[] nrms, int[] lab, int len, Func<int, bool> keep)
    {
      int m = 0;
      for (int i = 0; i < len; i++) if (keep(lab[i])) m++;
      if (m == 0) return;
      var p2 = new Point3d[m];
      var c2 = cols != null ? new System.Drawing.Color[m] : null;
      var n2 = nrms != null ? new Vector3d[m] : null;
      int k = 0;
      for (int i = 0; i < len; i++)
      {
        if (!keep(lab[i])) continue;
        p2[k] = pts[i];
        if (c2 != null) c2[k] = cols[i];
        if (n2 != null) n2[k] = nrms[i];
        k++;
      }
      if (c2 != null && n2 != null) target.AddRange(p2, n2, c2);
      else if (c2 != null) target.AddRange(p2, c2);
      else if (n2 != null) target.AddRange(p2, n2);
      else target.AddRange(p2);
    }
  }
}
