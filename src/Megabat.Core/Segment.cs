// Segment: a detected surface with plane, its points and orientation type
using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Megabat.Core
{
  public enum SegmentType { Unknown = -1, Horizontal = 0, Vertical = 1, Inclined = 2 }

  public class Segment
  {
    public Plane Plane;
    public PointCloud Points;      // not copied, only passed on
    public SegmentType Type = SegmentType.Unknown;
    public int Id = -1;            // order from Segmentize

    public Segment() { }
    public Segment(Plane plane, PointCloud points, SegmentType type, int id)
    {
      Plane = plane; Points = points; Type = type; Id = id;
    }

    // shallow copy: same point cloud
    public Segment Clone() { return new Segment(Plane, Points, Type, Id); }

    public static SegmentType TypeOf(Plane pl, double angTolDeg)
    {
      double t = angTolDeg * Math.PI / 180.0;
      double dz = Math.Abs(pl.Normal * Vector3d.ZAxis);
      if (dz >= Math.Cos(t)) return SegmentType.Horizontal;
      if (dz <= Math.Sin(t)) return SegmentType.Vertical;
      return SegmentType.Inclined;
    }

    public override string ToString()
    {
      string t = Type == SegmentType.Horizontal ? "horizontal" : Type == SegmentType.Vertical ? "vertical" : Type == SegmentType.Inclined ? "inclined" : "?";
      return string.Format("Segment {0} ({1}, {2} points)", Id, t, Points != null ? Points.Count : 0);
    }

    // For the cores working with separate lists
    public static void Split(IList<Segment> segs, out List<Plane> P, out List<int> T, out List<PointCloud> S, double angTolDeg = 10)
    {
      P = new List<Plane>(); T = new List<int>(); S = new List<PointCloud>();
      foreach (var s in segs)
      {
        if (s == null) continue;
        P.Add(s.Plane);
        T.Add((int)(s.Type == SegmentType.Unknown ? TypeOf(s.Plane, angTolDeg) : s.Type));
        S.Add(s.Points);
      }
    }
  }
}
