// Outline of a segment inside its rectangle: occupancy raster, closing, filling short gaps to the edges, cell-edge loops
using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace Megabat.Core
{
  public static class OutlineCore
  {
    public class Result
    {
      public List<Polyline> Loops = new List<Polyline>();   // outer outlines and openings, closed
      public int Nx, Ny; public double CellU, CellV;
      public double RawShare, ClosedShare;                  // occupied share of the raster before and after closing
    }

    // Rectangle: corner o, unit axes U, V, size W x H. cell = raster size, close = cells for closing gaps,
    // edge = max gap between points and rectangle edge that is filled
    public static Result Run(Point3d o, Vector3d U, Vector3d V, double W, double H, IEnumerable<Point3d> pts, double cell, int close, double edge)
    {
      var res = new Result();
      if (cell <= 0) cell = 0.05;
      int nx = Math.Max(1, (int)Math.Round(W / cell)), ny = Math.Max(1, (int)Math.Round(H / cell));
      double cw = W / nx, ch = H / ny;   // cells fit the rectangle exactly
      res.Nx = nx; res.Ny = ny; res.CellU = cw; res.CellV = ch;

      var g = new bool[nx, ny];
      foreach (var p in pts)
      {
        var d = p - o;
        int i = (int)Math.Floor(d * U / cw), j = (int)Math.Floor(d * V / ch);
        if (i >= 0 && j >= 0 && i < nx && j < ny) g[i, j] = true;
      }
      res.RawShare = (double)Count(g) / (nx * ny);

      if (close > 0) { g = Morph(g, close, true); g = Morph(g, close, false); }
      FillEdges(g, (int)Math.Round(edge / cw), (int)Math.Round(edge / ch));
      res.ClosedShare = (double)Count(g) / (nx * ny);

      foreach (var lp in Loops(g))
      {
        var p = new Polyline(lp.Select(q => o + q.X * cw * U + q.Y * ch * V));
        p.Add(p[0]);
        res.Loops.Add(p);
      }
      return res;
    }

    static int Count(bool[,] g) { int c = 0; foreach (var b in g) if (b) c++; return c; }

    // Square structuring element (2r+1); separable: rows then columns
    internal static bool[,] Morph(bool[,] g, int r, bool dilate)
    {
      int nx = g.GetLength(0), ny = g.GetLength(1);
      var a = new bool[nx, ny]; var b = new bool[nx, ny];
      for (int j = 0; j < ny; j++)
        for (int i = 0; i < nx; i++)
        {
          bool v = !dilate;
          for (int k = Math.Max(0, i - r); k <= Math.Min(nx - 1, i + r); k++)
            if (dilate ? g[k, j] : !g[k, j]) { v = dilate; break; }
          a[i, j] = v;
        }
      for (int i = 0; i < nx; i++)
        for (int j = 0; j < ny; j++)
        {
          bool v = !dilate;
          for (int k = Math.Max(0, j - r); k <= Math.Min(ny - 1, j + r); k++)
            if (dilate ? a[i, k] : !a[i, k]) { v = dilate; break; }
          b[i, j] = v;
        }
      return b;
    }

    static void FillEdges(bool[,] g, int ex, int ey)
    {
      int nx = g.GetLength(0), ny = g.GetLength(1);
      for (int j = 0; j < ny; j++)
      {
        int a = 0; while (a < nx && !g[a, j]) a++;
        if (a < nx && a <= ex) for (int i = 0; i < a; i++) g[i, j] = true;
        int b = nx - 1; while (b >= 0 && !g[b, j]) b--;
        if (b >= 0 && nx - 1 - b <= ex) for (int i = b + 1; i < nx; i++) g[i, j] = true;
      }
      for (int i = 0; i < nx; i++)
      {
        int a = 0; while (a < ny && !g[i, a]) a++;
        if (a < ny && a <= ey) for (int j = 0; j < a; j++) g[i, j] = true;
        int b = ny - 1; while (b >= 0 && !g[i, b]) b--;
        if (b >= 0 && ny - 1 - b <= ey) for (int j = b + 1; j < ny; j++) g[i, j] = true;
      }
    }

    // Directed cell edges with the occupied cell on the left, chained into loops, collinear vertices dropped
    static List<List<Point2d>> Loops(bool[,] g)
    {
      int nx = g.GetLength(0), ny = g.GetLength(1);
      bool Occ(int i, int j) => i >= 0 && j >= 0 && i < nx && j < ny && g[i, j];
      long K(int x, int y) => (long)x * 1000003L + y;
      var next = new Dictionary<long, List<(int x, int y)>>();
      void Add(int x0, int y0, int x1, int y1)
      {
        long k = K(x0, y0);
        if (!next.TryGetValue(k, out var l)) next[k] = l = new List<(int, int)>();
        l.Add((x1, y1));
      }
      for (int i = 0; i < nx; i++)
        for (int j = 0; j < ny; j++)
        {
          if (!g[i, j]) continue;
          if (!Occ(i, j - 1)) Add(i, j, i + 1, j);
          if (!Occ(i + 1, j)) Add(i + 1, j, i + 1, j + 1);
          if (!Occ(i, j + 1)) Add(i + 1, j + 1, i, j + 1);
          if (!Occ(i - 1, j)) Add(i, j + 1, i, j);
        }
      var res = new List<List<Point2d>>();
      while (next.Count > 0)
      {
        var startKey = next.Keys.First();
        int sx = (int)(startKey / 1000003L), sy = (int)(startKey % 1000003L);
        var pts = new List<(int x, int y)> { (sx, sy) };
        int cx = sx, cy = sy;
        for (int guard = 0; guard < 4 * (nx + 1) * (ny + 1); guard++)
        {
          long k = K(cx, cy);
          if (!next.TryGetValue(k, out var l)) break;
          var n = l[l.Count - 1]; l.RemoveAt(l.Count - 1);
          if (l.Count == 0) next.Remove(k);
          cx = n.x; cy = n.y;
          if (cx == sx && cy == sy) break;
          pts.Add((cx, cy));
        }
        var s = new List<Point2d>();
        for (int a = 0; a < pts.Count; a++)
        {
          var p0 = pts[(a - 1 + pts.Count) % pts.Count]; var p1 = pts[a]; var p2 = pts[(a + 1) % pts.Count];
          bool col = (p0.x == p1.x && p1.x == p2.x) || (p0.y == p1.y && p1.y == p2.y);
          if (!col) s.Add(new Point2d(p1.x, p1.y));
        }
        if (s.Count >= 3) res.Add(s);
      }
      return res;
    }
  }
}
