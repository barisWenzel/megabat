// Photogrammetry: COLMAP import, image board, rays, forward intersection, georeferencing
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Rhino.Geometry;

namespace Megabat.Core
{
  // One oriented image: camera plane (origin = projection centre, X/Y = image axes u/v, Z = viewing direction),
  // intrinsics in pixels and its rectangle on the image board (XY plane, u to +X, v to -Y)
  public class PhotoCam
  {
    public string Name, Path;
    public Plane Cam;
    public double Fx, Fy, Cx, Cy, W, H;
    public Rectangle3d Frame;

    public double Scale => Frame.Width / W;   // board units per pixel
    public BoundingBox Box => Frame.BoundingBox;

    public PhotoCam Clone() => (PhotoCam)MemberwiseClone();

    public string K => string.Format(CultureInfo.InvariantCulture, "{0} {1} {2} {3} {4} {5}", Fx, Fy, Cx, Cy, W, H);

    public static PhotoCam From(Plane cam, string k, Rectangle3d frame)
    {
      var v = k.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(t => double.Parse(t, CultureInfo.InvariantCulture)).ToArray();
      if (v.Length < 6) return null;
      return new PhotoCam { Cam = cam, Fx = v[0], Fy = v[1], Cx = v[2], Cy = v[3], W = v[4], H = v[5], Frame = frame };
    }

    public bool Contains(Point3d q)
    {
      var b = Box;
      return q.X >= b.Min.X && q.X <= b.Max.X && q.Y >= b.Min.Y && q.Y <= b.Max.Y;
    }

    // board point -> pixel (origin top left, v down)
    public void Pixel(Point3d q, out double u, out double v)
    {
      var b = Box; double s = Scale;
      u = (q.X - b.Min.X) / s; v = (b.Max.Y - q.Y) / s;
    }

    // board point -> unit ray direction in model space
    public Vector3d Ray(Point3d q)
    {
      Pixel(q, out double u, out double v);
      var d = (u - Cx) / Fx * Cam.XAxis + (v - Cy) / Fy * Cam.YAxis + Cam.ZAxis;
      d.Unitize(); return d;
    }

    // model point -> board point; false if behind the camera or outside the image
    public bool Project(Point3d p, out Point3d q)
    {
      var d = p - Cam.Origin; double z = d * Cam.ZAxis; q = Point3d.Unset;
      if (z <= 1e-9) return false;
      double u = Fx * (d * Cam.XAxis) / z + Cx, v = Fy * (d * Cam.YAxis) / z + Cy;
      if (u < 0 || v < 0 || u > W || v > H) return false;
      var b = Box; double s = Scale;
      q = new Point3d(b.Min.X + u * s, b.Max.Y - v * s, 0); return true;
    }

    // epipolar line of a ray in this image: projection of ray points from close to far (scale ~ scene size)
    public bool Epipolar(Point3d o, Vector3d d, double scale, out Line l)
    {
      l = Line.Unset; Point3d first = Point3d.Unset, last = Point3d.Unset;
      for (int i = 0; i <= 400; i++)
      {
        double t = scale * 0.01 * Math.Pow(1e5, i / 400.0);
        if (Project(o + t * d, out var q)) { if (!first.IsValid) first = q; last = q; }
      }
      if (!first.IsValid || first.DistanceTo(last) < 1e-9) return false;
      l = new Line(first, last); return true;
    }
  }

  public static class PhotoCore
  {
    // ---- COLMAP text model (cameras.txt, images.txt) ----
    public static List<PhotoCam> LoadColmap(string dir, string imgDir, List<string> notes)
    {
      var ci = CultureInfo.InvariantCulture;
      var cams = new Dictionary<string, double[]>();
      foreach (var l in File.ReadAllLines(System.IO.Path.Combine(dir, "cameras.txt")))
      {
        if (l.StartsWith("#") || l.Trim().Length == 0) continue;
        var t = l.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        double w = double.Parse(t[2], ci), h = double.Parse(t[3], ci);
        var p = t.Skip(4).Select(x => double.Parse(x, ci)).ToArray();
        string m = t[1];
        double fx, fy, cx, cy;
        if (m == "SIMPLE_PINHOLE" || m == "SIMPLE_RADIAL" || m == "RADIAL") { fx = fy = p[0]; cx = p[1]; cy = p[2]; }
        else { fx = p[0]; fy = p[1]; cx = p[2]; cy = p[3]; }
        if (m != "PINHOLE" && m != "SIMPLE_PINHOLE") notes.Add("camera " + t[0] + " is " + m + ": distortion ignored, use undistorted images");
        cams[t[0]] = new[] { fx, fy, cx, cy, w, h };
      }
      // images.txt: IMAGE_ID QW QX QY QZ TX TY TZ CAMERA_ID NAME, then one line of 2D points
      var lines = File.ReadAllLines(System.IO.Path.Combine(dir, "images.txt")).Where(l => !l.StartsWith("#")).ToList();
      var list = new List<PhotoCam>();
      for (int i = 0; i + 1 <= lines.Count; i += 2)
      {
        var t = lines[i].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (t.Length < 10) { i--; continue; }
        double qw = double.Parse(t[1], ci), qx = double.Parse(t[2], ci), qy = double.Parse(t[3], ci), qz = double.Parse(t[4], ci);
        var tv = new Vector3d(double.Parse(t[5], ci), double.Parse(t[6], ci), double.Parse(t[7], ci));
        // rows of R (world -> camera) are the camera axes in world coordinates
        var r0 = new Vector3d(1 - 2 * (qy * qy + qz * qz), 2 * (qx * qy - qz * qw), 2 * (qx * qz + qy * qw));
        var r1 = new Vector3d(2 * (qx * qy + qz * qw), 1 - 2 * (qx * qx + qz * qz), 2 * (qy * qz - qx * qw));
        var r2 = new Vector3d(2 * (qx * qz - qy * qw), 2 * (qy * qz + qx * qw), 1 - 2 * (qx * qx + qy * qy));
        var c = Point3d.Origin - (tv.X * r0 + tv.Y * r1 + tv.Z * r2);   // C = -R^T t
        if (!cams.TryGetValue(t[8], out var k)) continue;
        string name = string.Join(" ", t.Skip(9));
        string path = imgDir == null ? null : System.IO.Path.Combine(imgDir, name);
        if (path != null && !File.Exists(path)) path = null;
        list.Add(new PhotoCam { Name = name, Path = path, Cam = new Plane(c, r0, r1), Fx = k[0], Fy = k[1], Cx = k[2], Cy = k[3], W = k[4], H = k[5] });
      }
      return list.OrderBy(x => x.Name).ToList();
    }

    // Image board: rows of cols images in the XY plane, s = board units per pixel
    public static void Layout(List<PhotoCam> list, double s, int cols)
    {
      if (list.Count == 0) return;
      double gw = list.Max(x => x.W) * 1.1 * s, gh = list.Max(x => x.H) * 1.15 * s;
      for (int i = 0; i < list.Count; i++)
      {
        var m = list[i];
        double x0 = (i % cols) * gw, y0 = -(i / cols) * gh;
        m.Frame = new Rectangle3d(Plane.WorldXY, new Point3d(x0, y0 - m.H * s, 0), new Point3d(x0 + m.W * s, y0, 0));
      }
    }

    public static int CamAt(IList<PhotoCam> cams, Point3d q)
    {
      for (int i = 0; i < cams.Count; i++) if (cams[i].Contains(q)) return i;
      return -1;
    }

    // ---- forward intersection: least squares point closest to all rays ----
    public static Point3d Intersect(IList<Point3d> o, IList<Vector3d> d)
    {
      if (d.Count < 2) return Point3d.Unset;
      var A = new double[3, 3]; var r = new double[3];
      for (int i = 0; i < d.Count; i++)
      {
        double[] dd = { d[i].X, d[i].Y, d[i].Z }, cc = { o[i].X, o[i].Y, o[i].Z };
        for (int m = 0; m < 3; m++)
          for (int n = 0; n < 3; n++) { double a = (m == n ? 1 : 0) - dd[m] * dd[n]; A[m, n] += a; r[m] += a * cc[n]; }
      }
      return Solve3(A, r);
    }

    // largest intersection angle between two rays [°]
    public static double MaxAngle(IList<Vector3d> d)
    {
      double amax = 0;
      for (int i = 0; i < d.Count; i++)
        for (int j = i + 1; j < d.Count; j++)
        {
          double a = Vector3d.VectorAngle(d[i], d[j]) * 180 / Math.PI;
          amax = Math.Max(amax, Math.Min(a, 180 - a));
        }
      return amax;
    }

    static Point3d Solve3(double[,] A, double[] b)
    {
      double det = A[0, 0] * (A[1, 1] * A[2, 2] - A[1, 2] * A[2, 1]) - A[0, 1] * (A[1, 0] * A[2, 2] - A[1, 2] * A[2, 0]) + A[0, 2] * (A[1, 0] * A[2, 1] - A[1, 1] * A[2, 0]);
      if (Math.Abs(det) < 1e-12) return Point3d.Unset;
      double x = (b[0] * (A[1, 1] * A[2, 2] - A[1, 2] * A[2, 1]) - A[0, 1] * (b[1] * A[2, 2] - A[1, 2] * b[2]) + A[0, 2] * (b[1] * A[2, 1] - A[1, 1] * b[2])) / det;
      double y = (A[0, 0] * (b[1] * A[2, 2] - A[1, 2] * b[2]) - b[0] * (A[1, 0] * A[2, 2] - A[1, 2] * A[2, 0]) + A[0, 2] * (A[1, 0] * b[2] - b[1] * A[2, 0])) / det;
      double z = (A[0, 0] * (A[1, 1] * b[2] - b[1] * A[2, 1]) - A[0, 1] * (A[1, 0] * b[2] - b[1] * A[2, 0]) + b[0] * (A[1, 0] * A[2, 1] - A[1, 1] * A[2, 0])) / det;
      return new Point3d(x, y, z);
    }

    // ---- georeferencing ----

    // Helmert (similarity) transformation src -> dst, least squares (Umeyama). withScale = false: rigid.
    public static bool Helmert(IList<Point3d> src, IList<Point3d> dst, bool withScale, out Transform xf, out double scale)
    {
      xf = Transform.Identity; scale = 1;
      int n = Math.Min(src.Count, dst.Count);
      if (n < 3) return false;
      var cs = Centroid(src, n); var cd = Centroid(dst, n);
      // cross covariance  S = sum (d - cd)(s - cs)^T
      var S = new double[3, 3]; double vs = 0;
      for (int i = 0; i < n; i++)
      {
        var a = src[i] - cs; var b = dst[i] - cd;
        double[] av = { a.X, a.Y, a.Z }, bv = { b.X, b.Y, b.Z };
        for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++) S[r, c] += bv[r] * av[c];
        vs += a.SquareLength;
      }
      if (vs < 1e-20) return false;
      // rotation from the polar decomposition via the quaternion method (Horn): S -> 4x4 symmetric N, largest eigenvector
      double Sxx = S[0, 0], Sxy = S[0, 1], Sxz = S[0, 2], Syx = S[1, 0], Syy = S[1, 1], Syz = S[1, 2], Szx = S[2, 0], Szy = S[2, 1], Szz = S[2, 2];
      // Horn uses M = sum a b^T (src x dst); our S = sum b a^T = M^T
      double Mxx = Sxx, Mxy = Syx, Mxz = Szx, Myx = Sxy, Myy = Syy, Myz = Szy, Mzx = Sxz, Mzy = Syz, Mzz = Szz;
      var N = new double[4, 4]
      {
        { Mxx + Myy + Mzz, Myz - Mzy,        Mzx - Mxz,        Mxy - Myx },
        { Myz - Mzy,       Mxx - Myy - Mzz,  Mxy + Myx,        Mzx + Mxz },
        { Mzx - Mxz,       Mxy + Myx,       -Mxx + Myy - Mzz,  Myz + Mzy },
        { Mxy - Myx,       Mzx + Mxz,        Myz + Mzy,       -Mxx - Myy + Mzz }
      };
      var q = LargestEigenvector4(N);
      double w = q[0], x = q[1], y = q[2], z = q[3];
      var R = new double[3, 3]
      {
        { 1 - 2 * (y * y + z * z), 2 * (x * y - z * w),     2 * (x * z + y * w) },
        { 2 * (x * y + z * w),     1 - 2 * (x * x + z * z), 2 * (y * z - x * w) },
        { 2 * (x * z - y * w),     2 * (y * z + x * w),     1 - 2 * (x * x + y * y) }
      };
      if (withScale)
      {
        // s = sum b . (R a) / sum |a|^2
        double num = 0;
        for (int i = 0; i < n; i++)
        {
          var a = src[i] - cs; var b = dst[i] - cd;
          var ra = new Vector3d(R[0, 0] * a.X + R[0, 1] * a.Y + R[0, 2] * a.Z, R[1, 0] * a.X + R[1, 1] * a.Y + R[1, 2] * a.Z, R[2, 0] * a.X + R[2, 1] * a.Y + R[2, 2] * a.Z);
          num += b * ra;
        }
        scale = num / vs;
        if (scale <= 0) return false;
      }
      xf = Compose(R, scale, cs, cd);
      return true;
    }

    // Three points: p0 = origin, p1 on +X, p2 in the XY plane (+Y side); len > 0 = real distance p0-p1 for the scale
    public static bool ThreePoint(Point3d p0, Point3d p1, Point3d p2, double len, out Transform xf, out double scale)
    {
      xf = Transform.Identity; scale = 1;
      var X = p1 - p0; var Y0 = p2 - p0;
      if (X.Length < 1e-12) return false;
      var Z = Vector3d.CrossProduct(X, Y0);
      if (Z.Length < 1e-12 * X.Length * Y0.Length) return false;
      var pl = new Plane(p0, X, Vector3d.CrossProduct(Z, X));
      xf = Transform.PlaneToPlane(pl, Plane.WorldXY);
      if (len > 0) { scale = len / X.Length; xf = Transform.Scale(Point3d.Origin, scale) * xf; }
      return true;
    }

    static Point3d Centroid(IList<Point3d> p, int n)
    {
      double x = 0, y = 0, z = 0;
      for (int i = 0; i < n; i++) { x += p[i].X; y += p[i].Y; z += p[i].Z; }
      return new Point3d(x / n, y / n, z / n);
    }

    // x' = s R (x - cs) + cd
    static Transform Compose(double[,] R, double s, Point3d cs, Point3d cd)
    {
      var t = Transform.Identity;
      for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++) t[r, c] = s * R[r, c];
      double[] c0 = { cs.X, cs.Y, cs.Z }, d0 = { cd.X, cd.Y, cd.Z };
      for (int r = 0; r < 3; r++) t[r, 3] = d0[r] - (t[r, 0] * c0[0] + t[r, 1] * c0[1] + t[r, 2] * c0[2]);
      return t;
    }

    // Largest eigenvector of a symmetric 4x4 matrix: power iteration on N + c I (c makes it positive definite)
    static double[] LargestEigenvector4(double[,] N)
    {
      double c = 0;
      for (int i = 0; i < 4; i++) { double r = 0; for (int j = 0; j < 4; j++) r += Math.Abs(N[i, j]); c = Math.Max(c, r); }
      var v = new double[] { 1, 0.1, 0.01, 0.001 }; var w = new double[4];
      for (int it = 0; it < 2000; it++)
      {
        double len = 0;
        for (int i = 0; i < 4; i++) { w[i] = c * v[i]; for (int j = 0; j < 4; j++) w[i] += N[i, j] * v[j]; len += w[i] * w[i]; }
        len = Math.Sqrt(len); if (len < 1e-300) break;
        double diff = 0;
        for (int i = 0; i < 4; i++) { double x = w[i] / len; diff += Math.Abs(x - v[i]); v[i] = x; }
        if (diff < 1e-15) break;
      }
      return v;
    }
  }
}
