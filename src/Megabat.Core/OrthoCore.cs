// Orthophoto of a planar face (facade) from oriented photos: plane fit, raster on the plane, projection into the best photo
using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace Megabat.Core
{
  public static class OrthoCore
  {
    // Decoded image: 3 bytes per pixel (B, G, R), rows top to bottom, no padding
    public class Image { public int W, H; public byte[] Bgr; }

    public class Result
    {
      public Plane Plane;                 // target system: origin = lower left corner of the raster, X right, Y up
      public double Px;                   // pixel size in target units
      public int W, H;                    // raster size
      public byte[] Bgra;                 // W*H*4, rows top to bottom; alpha 0 outside the polygon or not seen
      public Polyline Outline;            // polygon projected onto the plane (target system), closed
      public double Rms;                  // distance of the points to the fitted plane (RMS, target units)
      public int Best = -1;               // index of the main photo
      public int[] Used;                  // number of pixels taken from each photo
      public int Seen, Inside;            // pixels with colour / inside the polygon
    }

    // pts: polygon corners in the target system (in order); toPhoto: target -> photo (COLMAP) system;
    // px <= 0: automatic (longest side 2000 px); force >= 0: use only this photo; load: decodes photo i
    // sort = true: corners ordered by angle around their centre (convex outlines), starting with pts[0]
    public static Result Run(IList<PhotoCam> cams, IList<Point3d> pts, Transform toPhoto, double px, int force, int maxSide, bool sort, Func<int, Image> load)
    {
      var r = new Result();
      if (pts.Count < 3) return null;
      Plane.FitPlaneToPoints(pts, out Plane fit, out double dev);
      if (sort)
      {
        var c0 = new Point3d(pts.Average(p => p.X), pts.Average(p => p.Y), pts.Average(p => p.Z));
        var fp = new Plane(c0, fit.XAxis, fit.YAxis);
        var ang = pts.Select(p => { fp.ClosestParameter(p, out double s, out double t); return Math.Atan2(t, s); }).ToList();
        double a0 = ang[0];
        pts = Enumerable.Range(0, pts.Count).OrderBy(i => (ang[i] - a0 + 4 * Math.PI) % (2 * Math.PI)).Select(i => pts[i]).ToList();
      }
      // X along the first edge, projected; Y up in the plane
      var x = pts[1] - pts[0]; x -= (x * fit.Normal) * fit.Normal;
      if (x.Length < 1e-12) return null;
      var pl = new Plane(fit.Origin, x, Vector3d.CrossProduct(fit.Normal, x));
      double sq = 0; var uv = new List<Point2d>();
      foreach (var p in pts)
      {
        pl.ClosestParameter(p, out double s, out double t); uv.Add(new Point2d(s, t));
        sq += Math.Pow(pl.DistanceTo(p), 2);
      }
      r.Rms = Math.Sqrt(sq / pts.Count);
      double u0 = uv.Min(q => q.X), u1 = uv.Max(q => q.X), v0 = uv.Min(q => q.Y), v1 = uv.Max(q => q.Y);
      double size = Math.Max(u1 - u0, v1 - v0);
      if (px <= 0) px = size / 2000.0;
      if (size / px > maxSide) px = size / maxSide;
      int W = Math.Max(1, (int)Math.Ceiling((u1 - u0) / px)), H = Math.Max(1, (int)Math.Ceiling((v1 - v0) / px));
      r.Plane = new Plane(pl.PointAt(u0, v0), pl.XAxis, pl.YAxis); r.Px = px; r.W = W; r.H = H;
      r.Outline = new Polyline(uv.Select(q => r.Plane.PointAt(q.X - u0, q.Y - v0))); r.Outline.Add(r.Outline[0]);
      var poly = uv.Select(q => new Point2d(q.X - u0, q.Y - v0)).ToList();

      // plane in the photo system: origin, axes (scaled) and normal
      var o = r.Plane.Origin; o.Transform(toPhoto);
      var ex = r.Plane.PointAt(1, 0); ex.Transform(toPhoto); var ax = ex - o;
      var ey = r.Plane.PointAt(0, 1); ey.Transform(toPhoto); var ay = ey - o;
      var n = Vector3d.CrossProduct(ax, ay); n.Unitize();
      Point3d P(double s, double t) => o + s * ax + t * ay;

      // pixels per unit for photo c at point q (0 if not visible)
      double Score(PhotoCam c, Point3d q)
      {
        if (!c.Project(q, out _)) return 0;
        var v = c.Cam.Origin - q; double d = v.Length; if (d < 1e-12) return 0;
        double cos = Math.Abs(v * n) / d;
        return c.Fx * cos / d;
      }

      // main photo: sees all corners, best score at the centre
      var cen = P((u1 - u0) / 2, (v1 - v0) / 2);
      var cornersPh = poly.Select(q => P(q.X, q.Y)).ToList();
      if (force >= 0 && force < cams.Count) r.Best = force;
      else
      {
        double bs = 0;
        for (int i = 0; i < cams.Count; i++)
        {
          if (cornersPh.Any(q => !cams[i].Project(q, out _))) continue;
          double s = Score(cams[i], cen); if (s > bs) { bs = s; r.Best = i; }
        }
      }

      // raster: main photo where visible, otherwise the best photo for this pixel
      var img = new Dictionary<int, Image>();
      Image Get(int i) { if (!img.TryGetValue(i, out var m)) img[i] = m = load(i); return m; }
      r.Bgra = new byte[W * H * 4]; r.Used = new int[cams.Count];
      // candidate photos: see at least one corner or the centre (keeps the per-pixel search short)
      var cand = Enumerable.Range(0, cams.Count).Where(i => force < 0 || i == force)
        .Where(i => cams[i].Project(cen, out _) || cornersPh.Any(q => cams[i].Project(q, out _))).ToList();
      for (int j = 0; j < H; j++)
      {
        double t = (H - j - 0.5) * px;
        for (int k = 0; k < W; k++)
        {
          double s = (k + 0.5) * px;
          if (!Inside(poly, s, t)) continue;
          r.Inside++;
          var q = P(s, t);
          int best = -1;
          if (r.Best >= 0 && cams[r.Best].Project(q, out _)) best = r.Best;
          else if (force < 0)
          {
            double bs = 0;
            foreach (int i in cand) { double sc = Score(cams[i], q); if (sc > bs) { bs = sc; best = i; } }
          }
          if (best < 0) continue;
          var c = cams[best]; var m = Get(best);
          if (m == null) continue;
          var d = q - c.Cam.Origin; double z = d * c.Cam.ZAxis;
          double uu = c.Fx * (d * c.Cam.XAxis) / z + c.Cx, vv = c.Fy * (d * c.Cam.YAxis) / z + c.Cy;
          // image may be stored at another resolution than the model (scale)
          uu *= m.W / c.W; vv *= m.H / c.H;
          Sample(m, uu - 0.5, vv - 0.5, r.Bgra, (j * W + k) * 4);
          r.Used[best]++; r.Seen++;
        }
      }
      return r;
    }

    static void Sample(Image m, double x, double y, byte[] dst, int o)
    {
      int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
      double fx = x - x0, fy = y - y0;
      int xa = Math.Clamp(x0, 0, m.W - 1), xb = Math.Clamp(x0 + 1, 0, m.W - 1), ya = Math.Clamp(y0, 0, m.H - 1), yb = Math.Clamp(y0 + 1, 0, m.H - 1);
      for (int ch = 0; ch < 3; ch++)
      {
        double a = m.Bgr[(ya * m.W + xa) * 3 + ch], b = m.Bgr[(ya * m.W + xb) * 3 + ch], c = m.Bgr[(yb * m.W + xa) * 3 + ch], e = m.Bgr[(yb * m.W + xb) * 3 + ch];
        dst[o + ch] = (byte)Math.Round((a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + e * fx) * fy);
      }
      dst[o + 3] = 255;
    }

    // even-odd rule
    static bool Inside(List<Point2d> poly, double x, double y)
    {
      bool inside = false;
      for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        if ((poly[i].Y > y) != (poly[j].Y > y) && x < (poly[j].X - poly[i].X) * (y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
          inside = !inside;
      return inside;
    }
  }
}
