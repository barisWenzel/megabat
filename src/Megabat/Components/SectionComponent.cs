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
  public class SectionComponent : MegabatComponent
  {
    public SectionComponent() : base("Section", "Section",
      "Architectural section through the cloud at any plane: cut lines from the segment planes, the rest traced or fitted by RANSAC; plus the view beyond.", Tab.Section) { }
    public override Guid ComponentGuid => new Guid("a3c9f0d2-11e4-4b6a-8a01-6d2f5e000031");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override string IconText => "Se";
    protected override Color IconColor => Icons.Section;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
      AddCloud(p, "C", "Point cloud (any size, read in blocks)");
      p.AddPlaneParameter("P", "P", "Section plane; plan = horizontal plane, longitudinal/cross section = vertical plane", GH_ParamAccess.item);
      p.AddNumberParameter("thick", "thick", "Thickness of the section band [m]", GH_ParamAccess.item, 0.04);
      p.AddNumberParameter("depth", "depth", "Depth of the view [m]: positive = along the normal of P, negative = opposite, 0 = none", GH_ParamAccess.item, 0);
      AddSegments(p, "Seg", "Optional: segments; each plane is intersected with the section plane and trimmed by its points", optional: true);
      p.AddNumberParameter("tol", "tol", "Line fit: max. distance of a point to the line [m]", GH_ParamAccess.item, 0.01);
      p.AddNumberParameter("minLen", "minLen", "Shortest line [m]", GH_ParamAccess.item, 0.1);
      p.AddNumberParameter("gap", "gap", "Gaps up to this length are bridged, corners up to this distance are joined [m]", GH_ParamAccess.item, 0.15);
      p.AddBooleanParameter("trace", "trace", "true = chain the points like by hand (polylines), false = straight lines by RANSAC", GH_ParamAccess.item, true);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
      p.AddCurveParameter("Cut", "Cut", "Cut lines: first from the planes, then the rest traced or fitted by RANSAC", GH_ParamAccess.list);
      AddCloud(p, "CutPts", "Points of the section band, projected into the plane");
      AddCloud(p, "Beyond", "Points beyond the section up to depth, in original position");
      p.AddTextParameter("Info", "Info", "Points in band and view, number of lines, unused points, run time", GH_ParamAccess.item);
    }
    protected override void SolveInstance(IGH_DataAccess DA)
    {
      CloudGoo cg = null; Plane P = Plane.Unset; var g = new List<SegmentGoo>();
      double thick = 0.04, depth = 0, tol = 0.01, minLen = 0.1, gap = 0.15; bool trace = true;
      if (!DA.GetData(0, ref cg) || cg?.Value == null) return;
      if (!DA.GetData(1, ref P) || !P.IsValid) return;
      DA.GetData(2, ref thick); DA.GetData(3, ref depth); DA.GetDataList(4, g);
      DA.GetData(5, ref tol); DA.GetData(6, ref minLen); DA.GetData(7, ref gap); DA.GetData(8, ref trace);
      List<Plane> Pl = null; List<PointCloud> S = null;
      var segs = Segs(g);
      if (segs.Count > 0) { Pl = segs.Select(s => s.Plane).ToList(); S = segs.Select(s => s.Points).ToList(); }
      var r = SectionCore.Run(cg.Value, P, Math.Max(1e-4, thick), depth,
        tol > 0 ? tol : 0.01, minLen > 0 ? minLen : 0.1, Math.Max(0, gap), trace, Pl, S);
      var cut = new List<Curve>();
      cut.AddRange(r.PlaneLines.Select(l => (Curve)new LineCurve(l)));
      cut.AddRange(r.Lines.Select(l => (Curve)new LineCurve(l)));
      cut.AddRange(r.Polys.Select(pl => (Curve)new PolylineCurve(pl)));
      DA.SetDataList(0, cut);
      DA.SetData(1, new CloudGoo(r.CutPts));
      DA.SetData(2, new CloudGoo(r.Beyond));
      DA.SetData(3, string.Format("Band {0:N0} pts, view {1:N0} pts, {2} plane lines, {3} further lines, unused {4:N0} pts, {5}",
        r.CutPts.Count, r.Beyond.Count, r.PlaneLines.Count, r.Lines.Count + r.Polys.Count, r.Unused, Ms));
    }
  }
}
