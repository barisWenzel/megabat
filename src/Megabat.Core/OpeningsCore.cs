// Openings in walls from the scan: empty regions in the occupancy raster of each wall rectangle
// (glass and open doors return no points), classified by sill height and by what lies behind them
using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace Megabat.Core
{
  public static class OpeningsCore
  {
    public class Options
    {
      public double Cell = 0.05, MinW = 0.4, MinH = 0.4, Sill = 0.3, Fill = 0.6, Depth = 1.5, Dense = 100;
      public int Close = 1;
    }

    public class Opening
    {
      public int Segment;                 // index in the segment list
      public Point3d[] Corners;           // lower left, lower right, upper right, upper left
      public double Width, Height, Sill;  // [m]; sill = height above the bottom of the wall
      public double Behind;               // points per m² of opening on the less dense side (0 = nothing seen through)
      public string Type;                 // window, door, full-height window, floor opening
    }

    // segs and their rectangles (same order, from SegmentRectCore); cloud = all points (for the view through the opening)
    public static List<Opening> Run(IList<Segment> segs, IList<SegmentRectCore.Rect> rects, PointCloud cloud, Options o)
    {
      var res = new List<Opening>();
      Point3d[] all = cloud?.GetPoints();
      for (int si = 0; si < segs.Count; si++)
      {
        var seg = segs[si]; var r = rects[si];
        if (seg.Type != SegmentType.Vertical || r.Width <= 0 || r.Height <= 0 || seg.Points == null) continue;
        var U = r.Frame.XAxis; var V = r.Frame.YAxis; var N = Vector3d.CrossProduct(U, V); N.Unitize();
        var o0 = r.P(r.S0, r.T0);
        int nx = Math.Max(1, (int)Math.Round(r.Width / o.Cell)), ny = Math.Max(1, (int)Math.Round(r.Height / o.Cell));
        double cw = r.Width / nx, ch = r.Height / ny;

        // occupancy, then closing to bridge gaps between scan lines
        var g = new bool[nx, ny];
        foreach (var it in seg.Points)
        {
          var d = it.Location - o0;
          int i = (int)Math.Floor(d * U / cw), j = (int)Math.Floor(d * V / ch);
          if (i >= 0 && j >= 0 && i < nx && j < ny) g[i, j] = true;
        }
        if (o.Close > 0) { g = OutlineCore.Morph(g, o.Close, true); g = OutlineCore.Morph(g, o.Close, false); }

        // empty regions (4-connected); an opening may touch the bottom edge (door) but not the sides or the top
        var lab = new int[nx, ny]; int next = 0;
        for (int i0 = 0; i0 < nx; i0++)
          for (int j0 = 0; j0 < ny; j0++)
          {
            if (g[i0, j0] || lab[i0, j0] != 0) continue;
            next++;
            var stack = new Stack<(int, int)>(); stack.Push((i0, j0)); lab[i0, j0] = next;
            int a0 = i0, a1 = i0, b0 = j0, b1 = j0, n = 0; bool side = false, top = false, bottom = false;
            while (stack.Count > 0)
            {
              var (i, j) = stack.Pop(); n++;
              a0 = Math.Min(a0, i); a1 = Math.Max(a1, i); b0 = Math.Min(b0, j); b1 = Math.Max(b1, j);
              if (i == 0 || i == nx - 1) side = true;
              if (j == ny - 1) top = true;
              if (j == 0) bottom = true;
              foreach (var (di, dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
              {
                int p = i + di, q = j + dj;
                if (p < 0 || q < 0 || p >= nx || q >= ny || g[p, q] || lab[p, q] != 0) continue;
                lab[p, q] = next; stack.Push((p, q));
              }
            }
            if (side || top) continue;
            double w = (a1 - a0 + 1) * cw, h = (b1 - b0 + 1) * ch;
            if (w < o.MinW || h < o.MinH) continue;
            if ((double)n / ((a1 - a0 + 1) * (b1 - b0 + 1)) < o.Fill) continue;   // not rectangular enough

            double s0 = r.S0 + a0 * cw, s1 = r.S0 + (a1 + 1) * cw, t0 = r.T0 + b0 * ch, t1 = r.T0 + (b1 + 1) * ch;
            var op = new Opening
            {
              Segment = si, Width = w, Height = h, Sill = bottom ? 0 : t0 - r.T0,
              Corners = new[] { r.P(s0, t0), r.P(s1, t0), r.P(s1, t1), r.P(s0, t1) }
            };

            // view through: points in front of and behind the opening, 0.3 m to Depth away from the wall,
            // inner 80 % of the width, from 0.2 m above the sill (no floor) to 0.1 m below the head
            if (all != null)
            {
              double m = 0.1 * w, zb = t0 + 0.2, zt = t1 - 0.1;
              int plus = 0, minus = 0;
              foreach (var p in all)
              {
                var d = p - r.Frame.Origin;
                double s = d * U, t = d * V, e = d * N;
                if (s < s0 + m || s > s1 - m || t < zb || t > zt) continue;
                double ae = Math.Abs(e);
                if (ae < 0.3 || ae > o.Depth) continue;
                if (e > 0) plus++; else minus++;
              }
              double area = Math.Max(1e-6, (w - 2 * m) * Math.Max(0.01, zt - zb));
              op.Behind = Math.Min(plus, minus) / area;
            }
            bool full = op.Sill < o.Sill;
            if (!full) op.Type = "window";
            else if (all == null) op.Type = "floor opening";
            else op.Type = op.Behind >= o.Dense ? "door" : op.Behind < o.Dense / 10 ? "full-height window" : "floor opening";
            res.Add(op);
          }
      }
      return res;
    }
  }
}
