// Level of detail for large point clouds: octree whose nodes hold a random sample each, finer points in the children
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using Rhino.Geometry;

namespace Megabat.Core
{
  public class LodNode
  {
    public BoundingBox Box;
    public PointCloud Cloud;
    public LodNode[] Kids;   // 8 octants, null where empty
    public int Level;
    internal List<int> Idx = new List<int>();
  }

  public static class LodCore
  {
    // cap = points per node, maxLevel = deepest level (leaves may hold more than cap)
    public static LodNode Build(PointCloud c, int cap = 20000, int maxLevel = 12)
    {
      if (c == null || c.Count == 0) return null;
      var pts = c.GetPoints();
      var cols = c.ContainsColors ? c.GetColors() : null;
      var nrm = c.ContainsNormals ? c.GetNormals() : null;
      int n = pts.Length;

      // random order: every node gets an even sample of its region
      var order = new int[n];
      for (int i = 0; i < n; i++) order[i] = i;
      var rnd = new Random(1);
      for (int i = n - 1; i > 0; i--) { int j = rnd.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }

      // cubic root box
      var bb = c.GetBoundingBox(true);
      double s = Math.Max(bb.Diagonal.X, Math.Max(bb.Diagonal.Y, bb.Diagonal.Z)) * 1.0001 + 1e-9;
      var root = new LodNode { Box = new BoundingBox(bb.Min, bb.Min + new Vector3d(s, s, s)), Level = 0 };

      foreach (int i in order)
      {
        var node = root; var p = pts[i];
        while (node.Idx.Count >= cap && node.Level < maxLevel)
        {
          var mid = node.Box.Center;
          int k = (p.X >= mid.X ? 1 : 0) | (p.Y >= mid.Y ? 2 : 0) | (p.Z >= mid.Z ? 4 : 0);
          if (node.Kids == null) node.Kids = new LodNode[8];
          var kid = node.Kids[k];
          if (kid == null)
          {
            var lo = node.Box.Min; double h = (node.Box.Max.X - lo.X) / 2;
            var o = new Point3d(lo.X + ((k & 1) != 0 ? h : 0), lo.Y + ((k & 2) != 0 ? h : 0), lo.Z + ((k & 4) != 0 ? h : 0));
            kid = node.Kids[k] = new LodNode { Box = new BoundingBox(o, o + new Vector3d(h, h, h)), Level = node.Level + 1 };
          }
          node = kid;
        }
        node.Idx.Add(i);
      }

      // node clouds
      var all = new List<LodNode>(); Collect(root, all);
      Parallel.ForEach(all, nd =>
      {
        int m = nd.Idx.Count;
        var p = new Point3d[m]; var cc = cols != null ? new Color[m] : null; var nn = nrm != null ? new Vector3d[m] : null;
        for (int j = 0; j < m; j++) { int i = nd.Idx[j]; p[j] = pts[i]; if (cc != null) cc[j] = cols[i]; if (nn != null) nn[j] = nrm[i]; }
        nd.Cloud = CloudUtil.Make(p, cc, nn);
        nd.Idx = null;
        // tight box for culling
        if (m > 0) nd.Box = new BoundingBox(p);
      });
      return root;
    }

    static void Collect(LodNode n, List<LodNode> all)
    {
      all.Add(n);
      if (n.Kids != null) foreach (var k in n.Kids) if (k != null) Collect(k, all);
    }

    public static int NodeCount(LodNode n)
    {
      if (n == null) return 0;
      int c = 1; if (n.Kids != null) foreach (var k in n.Kids) c += NodeCount(k);
      return c;
    }
  }
}
