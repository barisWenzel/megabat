using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Megabat.Core;
using Megabat.Types;

namespace Megabat.Components
{
  public class SegmentizeComponent : MegabatComponent
  {
    public SegmentizeComponent() : base("Segmentize", "Segmentize",
      "Find planes in the cloud (sequential RANSAC). Each plane becomes a segment with plane, points and type.", Tab.Segments) { }
    public override Guid ComponentGuid => new Guid("a3c9f0d2-11e4-4b6a-8a01-6d2f5e000011");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override string IconText => "Sg";
    protected override Color IconColor => Icons.Segments;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
      AddCloud(p, "C", "Point cloud, best thinned with Downsample first (a few 100 000 points)");
      p.AddNumberParameter("tol", "tol", "Max. distance of a point to the plane [m]", GH_ParamAccess.item, 0.015);
      p.AddIntegerParameter("minPts", "minPts", "Minimum number of points per segment", GH_ParamAccess.item, 60);
      p.AddIntegerParameter("maxPlanes", "maxPlanes", "Maximum number of planes to find", GH_ParamAccess.item, 15);
      p.AddIntegerParameter("iter", "iter", "Plane hypotheses per pass", GH_ParamAccess.item, 1000);
      p.AddIntegerParameter("sample", "sample", "Sample size used to score the hypotheses", GH_ParamAccess.item, 1000);
      p.AddIntegerParameter("seed", "seed", "Random seed (same value = same result)", GH_ParamAccess.item, 3);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
      AddSegments(p, "Segments", "Segments in order of detection, coloured; type preliminary with 10° tolerance");
      int r = AddCloud(p, "Rest", "Unassigned points (preview off by default)");
      if (p[r] is CloudParam cp) cp.Hidden = true;
      p.AddTextParameter("Info", "Info", "Number of segments, rest, run time", GH_ParamAccess.item);
    }
    protected override void SolveInstance(IGH_DataAccess DA)
    {
      CloudGoo cg = null; double tol = 0.015; int minPts = 60, maxPlanes = 15, iter = 1000, sample = 1000, seed = 3;
      if (!DA.GetData(0, ref cg) || cg?.Value == null) return;
      DA.GetData(1, ref tol); DA.GetData(2, ref minPts); DA.GetData(3, ref maxPlanes);
      DA.GetData(4, ref iter); DA.GetData(5, ref sample); DA.GetData(6, ref seed);
      var r = RansacCore.Run(cg.Value, tol, Math.Max(1, iter), Math.Max(3, minPts), Math.Max(1, maxPlanes), Math.Max(3, sample), seed);
      var segs = new List<Segment>();
      for (int i = 0; i < r.Planes.Count; i++)
        segs.Add(new Segment(r.Planes[i], r.Segments[i], Segment.TypeOf(r.Planes[i], 10), i));
      DA.SetDataList(0, Wrap(segs));
      DA.SetData(1, new CloudGoo(r.Rest));
      DA.SetData(2, string.Format("{0} segments, rest {1:N0} points, {2}", segs.Count, r.Rest.Count, Ms));
    }
  }

  public class ClassifyComponent : MegabatComponent
  {
    public ClassifyComponent() : base("Classify", "Classify",
      "Sort segments by orientation (horizontal, vertical, inclined) and optionally snap them to Z or to the main wall direction.", Tab.Segments) { }
    public override Guid ComponentGuid => new Guid("a3c9f0d2-11e4-4b6a-8a01-6d2f5e000012");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override string IconText => "Cl";
    protected override Color IconColor => Icons.Segments;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
      AddSegments(p, "Seg", "Segments from Segmentize");
      p.AddNumberParameter("angTol", "angTol", "Angle tolerance for horizontal/vertical and for snapping [°]", GH_ParamAccess.item, 10);
      p.AddBooleanParameter("snap", "snap", "true = snap normals to Z or to the main wall direction", GH_ParamAccess.item, true);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
      AddSegments(p, "Segments", "Segments with type, planes snapped if requested");
      p.AddVectorParameter("Dir", "Dir", "Main wall direction (mean modulo 90°)", GH_ParamAccess.item);
      p.AddTextParameter("Info", "Info", "Count per type, run time", GH_ParamAccess.item);
    }
    protected override void SolveInstance(IGH_DataAccess DA)
    {
      var g = new List<SegmentGoo>(); double angTol = 10; bool snap = true;
      if (!DA.GetDataList(0, g)) return;
      DA.GetData(1, ref angTol); DA.GetData(2, ref snap);
      var segs = Segs(g);
      if (segs.Count == 0) return;
      var r = ClassifyCore.Run(segs.Select(s => s.Plane).ToList(), angTol, snap);
      var outS = new List<Segment>();
      for (int i = 0; i < segs.Count; i++)
        outS.Add(new Segment(r.Planes[i], segs[i].Points, (SegmentType)r.Type[i], segs[i].Id));
      DA.SetDataList(0, Wrap(outS));
      DA.SetData(1, r.Dir);
      DA.SetData(2, string.Format("{0} horizontal, {1} vertical, {2} inclined, {3}",
        r.Type.Count(t => t == 0), r.Type.Count(t => t == 1), r.Type.Count(t => t == 2), Ms));
    }
  }

  public class DeconstructSegmentComponent : MegabatComponent
  {
    public DeconstructSegmentComponent() : base("Deconstruct Segment", "DeSeg",
      "Split a segment into plane, points, type and number.", Tab.Segments) { }
    public override Guid ComponentGuid => new Guid("a3c9f0d2-11e4-4b6a-8a01-6d2f5e000013");
    public override GH_Exposure Exposure => GH_Exposure.secondary;
    protected override string IconText => "DS";
    protected override Color IconColor => Icons.Segments;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
      p.AddParameter(new SegmentParam(), "Seg", "Seg", "Segment", GH_ParamAccess.item);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
      p.AddPlaneParameter("Plane", "Plane", "Plane of the segment", GH_ParamAccess.item);
      AddCloud(p, "Points", "Points of the segment");
      p.AddIntegerParameter("Type", "Type", "-1 = unknown, 0 = horizontal, 1 = vertical, 2 = inclined", GH_ParamAccess.item);
      p.AddIntegerParameter("Id", "Id", "Number from Segmentize", GH_ParamAccess.item);
    }
    protected override void SolveInstance(IGH_DataAccess DA)
    {
      SegmentGoo g = null;
      if (!DA.GetData(0, ref g) || g?.Value == null) return;
      DA.SetData(0, g.Value.Plane);
      DA.SetData(1, new CloudGoo(g.Value.Points));
      DA.SetData(2, (int)g.Value.Type);
      DA.SetData(3, g.Value.Id);
    }
  }
}
