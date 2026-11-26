// Mesh as point cloud and back: vertices with colour and normal as a cloud; after cropping or sectioning the cloud,
// the faces whose vertices are still in it are taken from the original mesh (no mesh split needed)
using System;
using System.Collections.Generic;
using System.Drawing;
using Rhino.Geometry;

namespace Megabat.Core
{
  public static class MeshCloudCore
  {
    // all vertices; colours if the mesh has them, normals always (computed on a copy if missing)
    public static PointCloud ToCloud(Mesh m)
    {
      var pc = new PointCloud();
      if (m == null || m.Vertices.Count == 0) return pc;
      int n = m.Vertices.Count;
      if (m.Normals.Count != n) { m = m.DuplicateMesh(); m.Normals.ComputeNormals(); }
      var pts = new Point3d[n]; var nrm = new Vector3d[n];
      for (int i = 0; i < n; i++) { pts[i] = m.Vertices.Point3dAt(i); nrm[i] = m.Normals[i]; }
      if (m.VertexColors.Count == n)
      {
        var col = new Color[n];
        for (int i = 0; i < n; i++) col[i] = m.VertexColors[i];
        pc.AddRange(pts, nrm, col);
      }
      else pc.AddRange(pts, nrm);
      return pc;
    }

    // faces of m whose vertices lie in the cloud (exact position, as written by ToCloud);
    // all = every vertex of the face must be in it, otherwise one is enough
    public static Mesh Extract(Mesh m, PointCloud c, bool all, out int kept)
    {
      kept = 0;
      if (m == null || c == null || c.Count == 0) return null;
      var set = new HashSet<Point3d>(c.GetPoints());
      int n = m.Vertices.Count;
      var inside = new bool[n];
      for (int i = 0; i < n; i++) if (set.Contains(m.Vertices.Point3dAt(i))) { inside[i] = true; kept++; }
      if (kept == 0) return null;

      var drop = new List<int>();
      for (int f = 0; f < m.Faces.Count; f++)
      {
        var F = m.Faces[f];
        int k = (inside[F.A] ? 1 : 0) + (inside[F.B] ? 1 : 0) + (inside[F.C] ? 1 : 0) + (F.IsQuad && inside[F.D] ? 1 : 0);
        int nv = F.IsQuad ? 4 : 3;
        if (all ? k < nv : k == 0) drop.Add(f);
      }
      if (drop.Count == m.Faces.Count) return null;
      var r = m.DuplicateMesh();
      if (drop.Count > 0) r.Faces.DeleteFaces(drop, true);
      return r;
    }

    // As Extract, but cut exactly at the closed Brep b: faces with all vertices in the cloud are kept whole,
    // only the border strip (some vertices in the cloud) is split at b and its pieces inside b are kept
    public static Mesh ExtractCut(Mesh m, PointCloud c, Brep b, double tol, out int kept)
    {
      kept = 0;
      if (m == null || c == null || c.Count == 0 || b == null) return null;
      var set = new HashSet<Point3d>(c.GetPoints());
      int n = m.Vertices.Count;
      var inside = new bool[n];
      for (int i = 0; i < n; i++) if (set.Contains(m.Vertices.Point3dAt(i))) { inside[i] = true; kept++; }
      if (kept == 0) return null;

      var dropInner = new List<int>(); var dropBorder = new List<int>();
      for (int f = 0; f < m.Faces.Count; f++)
      {
        var F = m.Faces[f];
        int k = (inside[F.A] ? 1 : 0) + (inside[F.B] ? 1 : 0) + (inside[F.C] ? 1 : 0) + (F.IsQuad && inside[F.D] ? 1 : 0);
        int nv = F.IsQuad ? 4 : 3;
        if (k < nv) dropInner.Add(f);                 // inner mesh: only faces fully inside
        if (k == 0 || k == nv) dropBorder.Add(f);     // border strip: faces partly inside
      }
      var inner = m.DuplicateMesh(); inner.Faces.DeleteFaces(dropInner, true);
      var r = inner;
      if (dropBorder.Count < m.Faces.Count)
      {
        var border = m.DuplicateMesh(); border.Faces.DeleteFaces(dropBorder, true);
        var cutter = new Mesh();
        foreach (var bm in Mesh.CreateFromBrep(b, MeshingParameters.QualityRenderMesh) ?? new Mesh[0]) cutter.Append(bm);
        var pieces = cutter.Faces.Count > 0 ? border.Split(cutter) : null;
        var all = new Mesh();
        if (pieces != null && pieces.Length > 0) foreach (var p in pieces) all.Append(p); else all.Append(border);
        // keep the faces whose centre lies in b
        var drop = new List<int>();
        for (int f = 0; f < all.Faces.Count; f++)
          if (!b.IsPointInside(all.Faces.GetFaceCenter(f), tol, false)) drop.Add(f);
        if (drop.Count > 0) all.Faces.DeleteFaces(drop, true);
        r.Append(all);
      }
      if (r.Faces.Count == 0) return null;
      r.Normals.ComputeNormals();
      return r;
    }

    // Light stand-in for a large mesh: vertex clustering on a voxel grid (vertices of a cell merged to their mean,
    // collapsed and duplicate triangles dropped). target = approximate face count; the voxel follows from the area.
    public static Mesh Cluster(Mesh m, int target, out double voxel)
    {
      voxel = 0;
      if (m == null || m.Faces.Count <= target) { var d = m?.DuplicateMesh(); return d; }
      double area = 0;
      for (int f = 0; f < m.Faces.Count; f++)
      {
        var F = m.Faces[f];
        var a = m.Vertices.Point3dAt(F.A); var b = m.Vertices.Point3dAt(F.B); var c = m.Vertices.Point3dAt(F.C);
        area += 0.5 * Vector3d.CrossProduct(b - a, c - a).Length;
        if (F.IsQuad) { var dd = m.Vertices.Point3dAt(F.D); area += 0.5 * Vector3d.CrossProduct(c - a, dd - a).Length; }
      }
      voxel = Math.Sqrt(2 * area / Math.Max(1, target));
      return Cluster(m, voxel);
    }

    // vertex clustering with a given voxel edge length
    public static Mesh Cluster(Mesh m, double v)
    {
      if (m == null || v <= 0) return m?.DuplicateMesh();
      var bb = m.GetBoundingBox(false);
      bool col = m.VertexColors.Count == m.Vertices.Count;
      var map = new Dictionary<long, int>(); var idx = new int[m.Vertices.Count];
      var sum = new List<double[]>();
      for (int i = 0; i < m.Vertices.Count; i++)
      {
        var p = m.Vertices.Point3dAt(i);
        long k = ((long)((p.X - bb.Min.X) / v) * 2097152L + (long)((p.Y - bb.Min.Y) / v)) * 2097152L + (long)((p.Z - bb.Min.Z) / v);
        if (!map.TryGetValue(k, out int ci)) { ci = sum.Count; map[k] = ci; sum.Add(new double[7]); }
        var s = sum[ci]; s[0] += p.X; s[1] += p.Y; s[2] += p.Z; s[3]++;
        if (col) { var cc = m.VertexColors[i]; s[4] += cc.R; s[5] += cc.G; s[6] += cc.B; }
        idx[i] = ci;
      }
      var r = new Mesh();
      foreach (var s in sum)
      {
        r.Vertices.Add(s[0] / s[3], s[1] / s[3], s[2] / s[3]);
        if (col) r.VertexColors.Add((int)(s[4] / s[3]), (int)(s[5] / s[3]), (int)(s[6] / s[3]));
      }
      var seen = new HashSet<(int, int, int)>();
      void Tri(int a, int b, int c)
      {
        if (a == b || b == c || a == c) return;
        int x = Math.Min(a, Math.Min(b, c)), z = Math.Max(a, Math.Max(b, c)), y = a + b + c - x - z;
        if (seen.Add((x, y, z))) r.Faces.AddFace(a, b, c);
      }
      foreach (var F in m.Faces)
      {
        Tri(idx[F.A], idx[F.B], idx[F.C]);
        if (F.IsQuad) Tri(idx[F.A], idx[F.C], idx[F.D]);
      }
      r.Normals.ComputeNormals();
      return r;
    }
  }
}
