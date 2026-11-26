// Fast tools for large meshes: crop at a closed Brep or at planes (only the border strip is split),
// closest points for many query points in parallel
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Rhino.Geometry;

namespace Megabat.Core
{
  public static class MeshToolsCore
  {
    public class CropResult { public Mesh Inside, Outside; public int Whole, Border; }

    // Region = closed Brep b, or the intersection of the positive sides of the planes (normal points into the region).
    // Faces wholly inside / outside are copied unchanged; only faces that may cross the boundary are split.
    public static CropResult Crop(Mesh m, Brep b, IList<Plane> planes, double tol, bool wantOutside = true)
    {
      var res = new CropResult();
      if (m == null || m.Faces.Count == 0) return res;

      // region as half-spaces (nx, ny, nz, d, outward: inside where n·p - d <= 0) or as closed mesh
      double[] h = null; Mesh solid = null;
      if (planes != null && planes.Count > 0)
      {
        var l = new List<double>();
        foreach (var p in planes) { var n = -p.Normal; n.Unitize(); l.Add(n.X); l.Add(n.Y); l.Add(n.Z); l.Add(n * (Vector3d)p.Origin); }
        h = l.ToArray();
      }
      else if (b != null && b.IsSolid)
      {
        h = ConvexPlanes(b);
        if (h == null)
        {
          solid = new Mesh();
          foreach (var bm in Mesh.CreateFromBrep(b, MeshingParameters.QualityRenderMesh) ?? new Mesh[0]) solid.Append(bm);
          solid.Weld(Math.PI);
          if (solid.Faces.Count == 0) return res;
        }
      }
      else return res;
      int np = h == null ? 0 : h.Length / 4;

      // vertex side: per plane for convex regions (bit set, outside of plane q), in/out for a solid mesh
      int nv = m.Vertices.Count;
      var outBits = h != null ? new ulong[nv] : null;
      var inside = new bool[nv];
      if (h != null)
      {
        if (np > 64) throw new ArgumentException("at most 64 planes");
        Parallel.For(0, nv, i =>
        {
          var p = m.Vertices.Point3dAt(i); ulong bits = 0;
          for (int q = 0; q < np; q++) if (h[4 * q] * p.X + h[4 * q + 1] * p.Y + h[4 * q + 2] * p.Z - h[4 * q + 3] > tol) bits |= 1UL << q;
          outBits[i] = bits; inside[i] = bits == 0;
        });
      }
      else
      {
        var bb = solid.GetBoundingBox(true); bb.Inflate(tol);
        Parallel.For(0, nv, () => solid.DuplicateMesh(), (i, st, s) =>
        {
          var p = m.Vertices.Point3dAt(i);
          inside[i] = bb.Contains(p) && s.IsPointInside(p, tol, false);
          return s;
        }, s => { });
      }

      // face class: 1 inside, 0 outside, 2 border
      int nf = m.Faces.Count;
      var cls = new byte[nf];
      Parallel.For(0, nf, f =>
      {
        var F = m.Faces[f];
        int k = (inside[F.A] ? 1 : 0) + (inside[F.B] ? 1 : 0) + (inside[F.C] ? 1 : 0) + (F.IsQuad && inside[F.D] ? 1 : 0);
        int n = F.IsQuad ? 4 : 3;
        if (k == n) { cls[f] = 1; return; }
        if (h != null)
        {
          // wholly outside if all vertices lie outside one and the same plane
          ulong all = outBits[F.A] & outBits[F.B] & outBits[F.C];
          if (F.IsQuad) all &= outBits[F.D];
          cls[f] = all != 0 ? (byte)0 : (byte)2;
        }
        else cls[f] = k == 0 ? (byte)0 : (byte)2;
      });

      res.Inside = Sub(m, cls, 1);
      res.Outside = wantOutside ? Sub(m, cls, 0) : null;
      int border0 = 0; for (int f = 0; f < nf; f++) if (cls[f] == 2) border0++;
      res.Whole = nf - border0;

      var border = Sub(m, cls, 2);
      res.Border = border.Faces.Count;
      if (border.Faces.Count > 0)
      {
        // split the strip: plane by plane, or with the solid mesh
        var pieces = new List<Mesh> { border };
        if (h != null)
          for (int q = 0; q < np; q++)
          {
            var pl = new Plane(new Point3d(h[4 * q] * h[4 * q + 3], h[4 * q + 1] * h[4 * q + 3], h[4 * q + 2] * h[4 * q + 3]), new Vector3d(h[4 * q], h[4 * q + 1], h[4 * q + 2]));
            var next = new List<Mesh>();
            foreach (var pc in pieces) { var s = pc.Split(pl); if (s != null && s.Length > 0) next.AddRange(s); else next.Add(pc); }
            pieces = next;
          }
        else { var s = border.Split(solid); if (s != null && s.Length > 0) pieces = s.ToList(); }

        var cut = new Mesh(); foreach (var pc in pieces) cut.Append(pc);
        // sort the pieces' faces by their centre
        var cin = new bool[cut.Faces.Count];
        if (h != null)
          Parallel.For(0, cin.Length, f =>
          {
            var c = cut.Faces.GetFaceCenter(f); bool ins = true;
            for (int q = 0; q < np && ins; q++) if (h[4 * q] * c.X + h[4 * q + 1] * c.Y + h[4 * q + 2] * c.Z - h[4 * q + 3] > tol) ins = false;
            cin[f] = ins;
          });
        else
          Parallel.For(0, cin.Length, () => solid.DuplicateMesh(), (f, st, s) => { cin[f] = s.IsPointInside(cut.Faces.GetFaceCenter(f), tol, false); return s; }, s => { });

        var cc = new byte[cin.Length]; for (int f = 0; f < cin.Length; f++) cc[f] = cin[f] ? (byte)1 : (byte)0;
        var a = Sub(cut, cc, 1);
        if (res.Inside.Normals.Count > 0) a.Normals.ComputeNormals();
        res.Inside.Append(a);
        if (wantOutside)
        {
          var o = Sub(cut, cc, 0);
          if (res.Outside.Normals.Count > 0) o.Normals.ComputeNormals();
          res.Outside.Append(o);
        }
      }
      return res;
    }

    // faces of class c as a new mesh, built directly from arrays (vertices renumbered; normals, colours,
    // texture coordinates taken over), much faster than duplicating and deleting faces
    static Mesh Sub(Mesh m, byte[] cls, byte c)
    {
      int nv = m.Vertices.Count, nf = m.Faces.Count;
      var map = new int[nv]; for (int i = 0; i < nv; i++) map[i] = -1;
      var faces = new List<MeshFace>(); var used = new List<int>();
      for (int f = 0; f < nf; f++)
      {
        if (cls[f] != c) continue;
        var F = m.Faces[f];
        int A = Map(F.A), B = Map(F.B), C = Map(F.C);
        faces.Add(F.IsQuad ? new MeshFace(A, B, C, Map(F.D)) : new MeshFace(A, B, C));
      }
      int Map(int i) { if (map[i] < 0) { map[i] = used.Count; used.Add(i); } return map[i]; }

      var r = new Mesh();
      if (used.Count == 0) return r;
      if (m.Vertices.UseDoublePrecisionVertices) { var p = new Point3d[used.Count]; for (int i = 0; i < p.Length; i++) p[i] = m.Vertices.Point3dAt(used[i]); r.Vertices.UseDoublePrecisionVertices = true; r.Vertices.AddVertices(p); }
      else { var p = new Point3f[used.Count]; for (int i = 0; i < p.Length; i++) p[i] = m.Vertices[used[i]]; r.Vertices.AddVertices(p); }
      if (m.Normals.Count == nv) { var n = new Vector3f[used.Count]; for (int i = 0; i < n.Length; i++) n[i] = m.Normals[used[i]]; r.Normals.AddRange(n); }
      if (m.VertexColors.Count == nv) { var k = new System.Drawing.Color[used.Count]; for (int i = 0; i < k.Length; i++) k[i] = m.VertexColors[used[i]]; r.VertexColors.AppendColors(k); }
      if (m.TextureCoordinates.Count == nv) { var t = new Point2f[used.Count]; for (int i = 0; i < t.Length; i++) t[i] = m.TextureCoordinates[used[i]]; r.TextureCoordinates.AddRange(t); }
      r.Faces.AddFaces(faces);
      return r;
    }

    // closest point on m for every query point, in parallel; maxDist <= 0 = unlimited (-1 for points farther away)
    public static void Closest(Mesh m, IList<Point3d> q, double maxDist, Point3d[] pts, double[] dist, int[] face)
    {
      if (m == null || q.Count == 0) return;
      m.ClosestMeshPoint(q[0], 0);   // builds the search tree once, before the threads use it
      Parallel.For(0, q.Count, i =>
      {
        var mp = m.ClosestMeshPoint(q[i], maxDist > 0 ? maxDist : 0);
        if (mp == null) { pts[i] = Point3d.Unset; dist[i] = -1; face[i] = -1; return; }
        pts[i] = mp.Point; dist[i] = q[i].DistanceTo(mp.Point); face[i] = mp.FaceIndex;
      });
    }

    // planar convex Brep: face planes with outward normal as (nx, ny, nz, d); otherwise null
    static double[] ConvexPlanes(Brep b)
    {
      var h = new List<double>();
      var amp = AreaMassProperties.Compute(b);
      Point3d c = amp != null ? amp.Centroid : b.GetBoundingBox(true).Center;
      foreach (var f in b.Faces)
      {
        if (!f.IsPlanar(1e-6) || !f.TryGetPlane(out Plane pl, 1e-6)) return null;
        var n = pl.Normal; n.Unitize();
        double d = n * (Vector3d)pl.Origin;
        if (n * (Vector3d)c - d > 0) { n = -n; d = -d; }
        h.Add(n.X); h.Add(n.Y); h.Add(n.Z); h.Add(d);
      }
      double eps = 1e-6 * Math.Max(1, b.GetBoundingBox(true).Diagonal.Length);
      foreach (var v in b.Vertices)
        for (int q = 0; q < h.Count; q += 4)
          if (h[q] * v.Location.X + h[q + 1] * v.Location.Y + h[q + 2] * v.Location.Z - h[q + 3] > eps) return null;
      return h.Count / 4 <= 64 ? h.ToArray() : null;
    }
  }
}
