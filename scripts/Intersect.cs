// Megabat test: forward intersection of image measurements
// Inputs:  P (tree, one branch per 3D point, clicks on the image board, from Measure),
//          Cam, K, Frame (lists from ImportColmap)
// Outputs: Pt (3D point per branch, in COLMAP units), Rays (lines camera -> point), Res (distance of each ray to the point),
//          Angle (largest intersection angle between two rays [°]), Info
#region Usings
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Rhino;
using Rhino.Geometry;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
#endregion

public class Script_Instance : GH_ScriptInstance
{
  private void RunScript(DataTree<object> P, List<object> Cam, List<object> K, List<object> Frame,
    ref object Pt, ref object Rays, ref object Res, ref object Angle, ref object Info)
  {
    Component.Message = "Intersect";
    if (P == null || Cam == null || K == null || Frame == null || Cam.Count == 0) { Info = "connect P, Cam, K, Frame"; return; }
    var ci = CultureInfo.InvariantCulture;
    var cams = Cam.Select(Unwrap).Cast<Plane>().ToList();
    var ks = K.Select(x => Unwrap(x).ToString().Split(' ').Select(t => double.Parse(t, ci)).ToArray()).ToList();
    var frames = Frame.Select(Unwrap).Cast<Rectangle3d>().ToList();

    var pts = new List<Point3d>(); var rays = new DataTree<Line>(); var res = new DataTree<double>(); var ang = new List<double>();
    var notes = new List<string>();
    for (int b = 0; b < P.BranchCount; b++)
    {
      var path = P.Paths[b];
      var org = new List<Point3d>(); var dir = new List<Vector3d>();
      foreach (var o in P.Branch(path))
      {
        var q = (Point3d)Unwrap(o);
        int j = frames.FindIndex(f => f.Contains(q) == PointContainment.Inside);
        if (j < 0) { notes.Add(path + ": click outside all images"); continue; }
        var f0 = frames[j]; var k = ks[j];
        double s = f0.Width / k[4];                                   // document units per pixel
        var bb = f0.BoundingBox;
        double u = (q.X - bb.Min.X) / s, v = (bb.Max.Y - q.Y) / s;   // pixel coordinates, origin top left, v down
        var c = cams[j];
        var d = (u - k[2]) / k[0] * c.XAxis + (v - k[3]) / k[1] * c.YAxis + c.ZAxis;
        d.Unitize();
        org.Add(c.Origin); dir.Add(d);
      }
      if (dir.Count < 2) { pts.Add(Point3d.Unset); ang.Add(0); notes.Add(path + ": fewer than 2 rays"); continue; }

      // least squares: sum (I - d d^T) (p - c) = 0
      double[,] A = new double[3, 3]; double[] r = new double[3];
      for (int i = 0; i < dir.Count; i++)
      {
        double[] dd = { dir[i].X, dir[i].Y, dir[i].Z }, cc = { org[i].X, org[i].Y, org[i].Z };
        for (int m = 0; m < 3; m++)
          for (int n = 0; n < 3; n++)
          {
            double a = (m == n ? 1 : 0) - dd[m] * dd[n];
            A[m, n] += a; r[m] += a * cc[n];
          }
      }
      var p = Solve(A, r);
      pts.Add(p);
      double amax = 0;
      for (int i = 0; i < dir.Count; i++)
      {
        rays.Add(new Line(org[i], p), path);
        double t = (p - org[i]) * dir[i];
        res.Add(p.DistanceTo(org[i] + t * dir[i]), path);
        for (int jj = i + 1; jj < dir.Count; jj++)
        {
          double a = Vector3d.VectorAngle(dir[i], dir[jj]) * 180 / Math.PI;
          amax = Math.Max(amax, Math.Min(a, 180 - a));
        }
      }
      ang.Add(Math.Round(amax, 2));
    }
    Pt = pts; Rays = rays; Res = res; Angle = ang;
    Info = string.Format("{0} points, {1} rays{2}", pts.Count(x => x.IsValid), rays.DataCount, notes.Count > 0 ? "; " + string.Join("; ", notes) : "");
  }

  static object Unwrap(object o) { return o is Grasshopper.Kernel.Types.IGH_Goo g ? g.ScriptVariable() : o; }

  static Point3d Solve(double[,] A, double[] b)
  {
    double det = A[0, 0] * (A[1, 1] * A[2, 2] - A[1, 2] * A[2, 1]) - A[0, 1] * (A[1, 0] * A[2, 2] - A[1, 2] * A[2, 0]) + A[0, 2] * (A[1, 0] * A[2, 1] - A[1, 1] * A[2, 0]);
    if (Math.Abs(det) < 1e-12) return Point3d.Unset;
    double x = (b[0] * (A[1, 1] * A[2, 2] - A[1, 2] * A[2, 1]) - A[0, 1] * (b[1] * A[2, 2] - A[1, 2] * b[2]) + A[0, 2] * (b[1] * A[2, 1] - A[1, 1] * b[2])) / det;
    double y = (A[0, 0] * (b[1] * A[2, 2] - A[1, 2] * b[2]) - b[0] * (A[1, 0] * A[2, 2] - A[1, 2] * A[2, 0]) + A[0, 2] * (A[1, 0] * b[2] - b[1] * A[2, 0])) / det;
    double z = (A[0, 0] * (A[1, 1] * b[2] - b[1] * A[2, 1]) - A[0, 1] * (A[1, 0] * b[2] - b[1] * A[2, 0]) + b[0] * (A[1, 0] * A[2, 1] - A[1, 1] * A[2, 0])) / det;
    return new Point3d(x, y, z);
  }
}
