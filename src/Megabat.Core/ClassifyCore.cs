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
  public static class ClassifyCore
  {
    public class Result
    {
      public List<Plane> Planes = new List<Plane>();
      public int[] Type;
      public Vector3d Dir;
    }

    public static Result Run(List<Plane> P, double angTol, bool snap)
    {
      var res = new Result();
      double tol = angTol * Math.PI / 180.0;
      double cosT = Math.Cos(tol), sinT = Math.Sin(tol);
      var Z = Vector3d.ZAxis;

      // Determine type
      var type = new int[P.Count];
      double C4 = 0, S4 = 0;
      for (int i = 0; i < P.Count; i++)
      {
        Vector3d n = P[i].Normal;
        double dz = Math.Abs(n * Z);
        if (dz >= cosT) type[i] = 0;
        else if (dz <= sinT)
        {
          type[i] = 1;
          double a = Math.Atan2(n.Y, n.X);
          C4 += Math.Cos(4 * a); S4 += Math.Sin(4 * a);
        }
        else type[i] = 2;
      }

      // Main wall direction (mean modulo 90°)
      double th0 = Math.Atan2(S4, C4) / 4.0;
      var dx = new Vector3d(Math.Cos(th0), Math.Sin(th0), 0);
      var dy = Vector3d.CrossProduct(Z, dx);

      for (int i = 0; i < P.Count; i++)
      {
        Plane p = P[i];
        if (!snap || type[i] == 2) { res.Planes.Add(p); continue; }

        Point3d o = p.Origin;
        Vector3d n = p.Normal;

        if (type[i] == 0)
        {
          res.Planes.Add(n.Z > 0 ? new Plane(o, dx, dy) : new Plane(o, dy, dx));
        }
        else
        {
          double a = Math.Atan2(n.Y, n.X);
          double k = Math.Round((a - th0) / (Math.PI / 2));
          double tgt = th0 + k * Math.PI / 2;
          Vector3d nh = Math.Abs(a - tgt) < tol
            ? new Vector3d(Math.Cos(tgt), Math.Sin(tgt), 0)
            : new Vector3d(n.X, n.Y, 0);
          nh.Unitize();
          res.Planes.Add(new Plane(o, Vector3d.CrossProduct(Z, nh), Z));
        }
      }

      res.Type = type;
      res.Dir = dx;
      return res;
    }
  }
}
