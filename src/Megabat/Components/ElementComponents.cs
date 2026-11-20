using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Rhino;
using Rhino.Geometry;
using Megabat.Core;
using Megabat.Types;

namespace Megabat.Components
{
  public class SnapPolygonComponent : MegabatComponent
  {
    public SnapPolygonComponent() : base("SnapPolygon", "SnapPoly",
      "Pull a roughly sketched room outline or wall run onto the walls of the cloud and output it as a room volume or wall surface.", Tab.Elements) { }
    public override Guid ComponentGuid => new Guid("a3c9f0d2-11e4-4b6a-8a01-6d2f5e000021");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override string IconText => "SP";
    protected override Color IconColor => Icons.Elements;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
      p.AddCurveParameter("G", "G", "Rough outline as polyline (plan view); closed = room, open see open", GH_ParamAccess.item);
      AddSegments(p, "Seg", "Segments from Segmentize or Classify");
      AddCloud(p, "C", "Optional: point cloud for edges without a matching plane; a line is fitted locally into the wall points there", optional: true);
      int zb = p.AddNumberParameter("zBot", "zBot", "Optional: bottom [m]; otherwise lowest horizontal plane inside the outline, otherwise bottom of the largest segment", GH_ParamAccess.item);
      int zt = p.AddNumberParameter("zTop", "zTop", "Optional: top [m]; otherwise highest horizontal plane inside the outline, otherwise top of the largest segment", GH_ParamAccess.item);
      Optional(p, zb, zt);
      p.AddBooleanParameter("open", "open", "true = open wall run: no closing edge, Room is the wall surface without caps", GH_ParamAccess.item, false);
      p.AddNumberParameter("dTol", "dTol", "Max. distance between polygon edge and wall [m]", GH_ParamAccess.item, 0.3);
      p.AddNumberParameter("angTol", "angTol", "Max. angle between polygon edge and wall [°]", GH_ParamAccess.item, 10);
      p.AddNumberParameter("minSup", "minSup", "Minimum share of the edge length that must be covered by wall points", GH_ParamAccess.item, 0.3);
      p.AddIntegerParameter("sample", "sample", "Sample size per wall for the coverage test", GH_ParamAccess.item, 5000);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
      p.AddCurveParameter("Polygon", "Polygon", "Snapped outline at floor level", GH_ParamAccess.item);
      p.AddBrepParameter("Room", "Room", "Room volume between bottom and top; with open only the wall surface", GH_ParamAccess.item);
      p.AddTextParameter("Info", "Info", "Per edge: assigned wall (segment number, fitted locally or none), distance of the sketch, coverage, plumb deviation top minus bottom and flatness (5 to 95 %) [m]; heights, run time", GH_ParamAccess.list);
    }
    protected override void SolveInstance(IGH_DataAccess DA)
    {
      Curve G = null; var g = new List<SegmentGoo>(); CloudGoo cg = null;
      double zBot = double.NaN, zTop = double.NaN, dTol = 0.3, angTol = 10, minSup = 0.3; bool open = false; int sample = 5000;
      if (!DA.GetData(0, ref G)) return;
      if (!DA.GetDataList(1, g)) return;
      DA.GetData(2, ref cg);
      if (!DA.GetData(3, ref zBot)) zBot = double.NaN;
      if (!DA.GetData(4, ref zTop)) zTop = double.NaN;
      DA.GetData(5, ref open); DA.GetData(6, ref dTol); DA.GetData(7, ref angTol); DA.GetData(8, ref minSup); DA.GetData(9, ref sample);
      if (!G.TryGetPolyline(out Polyline pl)) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "G must be a polyline"); return; }
      var segs = Segs(g);
      if (segs.Count == 0) return;
      Segment.Split(segs, out var P, out var T, out var S, angTol);
      double tol = RhinoDoc.ActiveDoc != null ? RhinoDoc.ActiveDoc.ModelAbsoluteTolerance : 0.001;
      var r = SnapPolygonCore.Run(pl, P, T, S, cg?.Value, angTol, dTol, minSup, sample, tol, open, zBot, zTop);
      DA.SetData(0, r.Polygon);
      DA.SetData(1, r.Room);
      var info = new List<string>(r.Info);
      info.Add(Ms);
      DA.SetDataList(2, info);
    }
  }

  public class SnapOpeningComponent : MegabatComponent
  {
    public SnapOpeningComponent() : base("SnapOpening", "SnapOpen",
      "Pull a roughly sketched window or door rectangle onto the opening in the wall and measure the reveal depth.", Tab.Elements) { }
    public override Guid ComponentGuid => new Guid("a3c9f0d2-11e4-4b6a-8a01-6d2f5e000022");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override string IconText => "SO";
    protected override Color IconColor => Icons.Elements;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
      p.AddCurveParameter("G", "G", "Rough rectangle in the wall plane", GH_ParamAccess.item);
      AddSegments(p, "Seg", "Segments from Segmentize or Classify (for the host wall)", optional: true);
      AddCloud(p, "C", "Point cloud (thinned is fine)");
      p.AddNumberParameter("dTol", "dTol", "Half thickness of the band around the wall plane in which points count as wall [m]", GH_ParamAccess.item, 0.03);
      p.AddNumberParameter("search", "search", "Search distance around each sketched edge [m]; if no plane is closer, the wall is fitted locally from C", GH_ParamAccess.item, 0.3);
      p.AddNumberParameter("cell", "cell", "Grid size of the occupancy test [m], about twice the point spacing", GH_ParamAccess.item, 0.03);
      p.AddNumberParameter("minStep", "minStep", "Minimum jump in occupancy for an edge to count as found (0 to 1)", GH_ParamAccess.item, 0.2);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
      p.AddCurveParameter("Rect", "Rect", "Snapped rectangle in the wall plane", GH_ParamAccess.item);
      p.AddBrepParameter("Opening", "Opening", "Opening volume through the reveal", GH_ParamAccess.item);
      p.AddTextParameter("Info", "Info", "Width × height, reveal depth, shift per edge against the sketch [m], host wall, run time", GH_ParamAccess.list);
    }
    protected override void SolveInstance(IGH_DataAccess DA)
    {
      Curve G = null; var g = new List<SegmentGoo>(); CloudGoo cg = null;
      double dTol = 0.03, search = 0.3, cell = 0.03, minStep = 0.2;
      if (!DA.GetData(0, ref G)) return;
      DA.GetDataList(1, g);
      if (!DA.GetData(2, ref cg) || cg?.Value == null) return;
      DA.GetData(3, ref dTol); DA.GetData(4, ref search); DA.GetData(5, ref cell); DA.GetData(6, ref minStep);
      if (!G.TryGetPolyline(out Polyline pl)) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "G must be a polyline"); return; }
      Segment.Split(Segs(g), out var P, out var T, out var S);
      double tol = RhinoDoc.ActiveDoc != null ? RhinoDoc.ActiveDoc.ModelAbsoluteTolerance : 0.001;
      var r = SnapOpeningCore.Run(pl, P, T, cg.Value, dTol, search, cell, minStep, tol);
      DA.SetData(0, r.Rect);
      DA.SetData(1, r.Opening);
      var info = new List<string>();
      if (r.Size != null && r.Size.Count >= 2) info.Add(string.Format("Size {0:0.000} × {1:0.000} m", r.Size[0], r.Size[1]));
      info.Add("Reveal depth " + (double.IsNaN(r.Depth) ? "not found" : r.Depth.ToString("0.000") + " m"));
      info.AddRange(r.Info);
      info.Add(Ms);
      DA.SetDataList(2, info);
    }
  }

  public class FitColumnComponent : MegabatComponent
  {
    public FitColumnComponent() : base("FitColumn", "Column",
      "Round column by circle fit in plan (RANSAC and geometric least squares).", Tab.Elements) { }
    public override Guid ComponentGuid => new Guid("a3c9f0d2-11e4-4b6a-8a01-6d2f5e000023");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override string IconText => "FC";
    protected override Color IconColor => Icons.Elements;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
      AddCloud(p, "C", "Points of the column, e.g. cut out with CropBrep");
      p.AddNumberParameter("tol", "tol", "Max. distance of a point to the circle [m]", GH_ParamAccess.item, 0.01);
      p.AddIntegerParameter("iter", "iter", "RANSAC iterations", GH_ParamAccess.item, 500);
      p.AddIntegerParameter("seed", "seed", "Random seed", GH_ParamAccess.item, 1);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
      p.AddCircleParameter("Circle", "Circle", "Circle at the column base", GH_ParamAccess.item);
      p.AddBrepParameter("Cylinder", "Cylinder", "Cylinder over the height of the inliers", GH_ParamAccess.item);
      AddCloud(p, "Inliers", "Points on the circle");
      p.AddTextParameter("Info", "Info", "Radius, diameter, height, inliers, RMS of the radial deviation, covered circumference (below 90° unreliable), run time", GH_ParamAccess.item);
    }
    protected override void SolveInstance(IGH_DataAccess DA)
    {
      CloudGoo cg = null; double tol = 0.01; int iter = 500, seed = 1;
      if (!DA.GetData(0, ref cg) || cg?.Value == null || cg.Value.Count < 3) return;
      DA.GetData(1, ref tol); DA.GetData(2, ref iter); DA.GetData(3, ref seed);
      var C = cg.Value;
      var r = FitColumnCore.Run(C.GetPoints(), tol > 0 ? tol : 0.01, iter > 0 ? iter : 500, seed);
      if (r == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No circle found"); return; }
      if (r.Coverage < 90) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Low coverage: " + r.Coverage.ToString("0") + "°");
      var pl = new Plane(new Point3d(r.Cx, r.Cy, r.Z0), Vector3d.ZAxis);
      var circ = new Circle(pl, r.R);
      DA.SetData(0, circ);
      DA.SetData(1, new Cylinder(circ, r.Z1 - r.Z0).ToBrep(true, true));
      DA.SetData(2, new CloudGoo(FitColumnCore.Subset(C, r.Mask)));
      DA.SetData(3, string.Format("r = {0:0.000} m, d = {1:0.000} m, h = {2:0.000} m, inliers {3}/{4}, RMS {5:0.0000} m, coverage {6:0}°, {7}",
        r.R, 2 * r.R, r.Z1 - r.Z0, r.Count, C.Count, r.Rms, r.Coverage, Ms));
    }
  }

  public class SegmentRectComponent : MegabatComponent
  {
    public SegmentRectComponent() : base("SegmentRect", "SegRect",
      "One rectangular surface per segment: walls horizontal/vertical, inclined faces along their horizontal line, floors and ceilings along the wall direction. Edges are trimmed to the neighbouring planes (wall to wall, wall to ceiling).", Tab.Elements) { }
    public override Guid ComponentGuid => new Guid("a3c9f0d2-11e4-4b6a-8a01-6d2f5e000024");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override string IconText => "SR";
    protected override Color IconColor => Icons.Elements;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
      AddSegments(p, "Seg", "Segments from Segmentize or Classify");
      int d = p.AddVectorParameter("Dir", "Dir", "Optional: main direction for floors and ceilings, e.g. Dir from Classify; otherwise taken from the walls, without walls the minimum-area rectangle per face", GH_ParamAccess.item);
      Optional(p, d);
      p.AddNumberParameter("q", "q", "Ignored outliers per side [%]; 0 = extreme points", GH_ParamAccess.item, 1);
      p.AddBooleanParameter("trim", "trim", "true = move edges to the intersection line with a neighbouring plane", GH_ParamAccess.item, true);
      p.AddNumberParameter("gap", "gap", "Max. distance between edge and neighbour plane for trimming [m]", GH_ParamAccess.item, 0.3);
      p.AddNumberParameter("angTol", "angTol", "Max. angle between edge and intersection line [°]", GH_ParamAccess.item, 10);
      p.AddIntegerParameter("sample", "sample", "Points used per segment", GH_ParamAccess.item, 20000);
      p.AddNumberParameter("cell", "cell", "Raster size for the outline [m]; 0 = no outline (Shape stays empty)", GH_ParamAccess.item, 0);
      p.AddIntegerParameter("close", "close", "Outline: cells for closing gaps between points", GH_ParamAccess.item, 2);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
      p.AddBrepParameter("Srf", "Srf", "Rectangular surface per segment", GH_ParamAccess.list);
      p.AddCurveParameter("Rect", "Rect", "Outline per segment", GH_ParamAccess.list);
      p.AddNumberParameter("Cov", "Cov", "Coverage: share of the rectangle covered by points (0 to 1). Low values point to openings, L-shapes or occlusion", GH_ParamAccess.list);
      p.AddTextParameter("Info", "Info", "Per segment: size, orientation rule, trimmed edges with neighbour segment, coverage; run time", GH_ParamAccess.list);
      p.AddBrepParameter("Shape", "Shape", "Only with cell > 0: rectangle cut to the outline of the points (L-shapes, openings as holes), one branch per segment", GH_ParamAccess.tree);
    }
    protected override void SolveInstance(IGH_DataAccess DA)
    {
      var g = new List<SegmentGoo>(); Vector3d dir = Vector3d.Unset; double q = 1, gap = 0.3, angTol = 10; bool trim = true; int sample = 20000;
      if (!DA.GetDataList(0, g)) return;
      DA.GetData(1, ref dir); DA.GetData(2, ref q); DA.GetData(3, ref trim); DA.GetData(4, ref gap); DA.GetData(5, ref angTol); DA.GetData(6, ref sample);
      double cell = 0; int close = 2; DA.GetData(7, ref cell); DA.GetData(8, ref close);
      var shape = new Grasshopper.DataTree<Brep>(); double tol = RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.001;
      var segs = Segs(g).Where(x => x.Points != null && x.Points.Count >= 3).ToList();
      if (segs.Count == 0) return;
      var rs = SegmentRectCore.Run(segs, dir, q / 100.0, trim, gap, angTol, Math.Max(100, sample));
      var srf = new List<Brep>(); var crv = new List<Curve>(); var cov = new List<double>(); var info = new List<string>();
      string[] en = { "start", "end", "bottom", "top" };
      for (int i = 0; i < rs.Count; i++)
      {
        var r = rs[i]; var c = r.Corners();
        srf.Add(r.Width > 0 && r.Height > 0 ? Brep.CreateFromCornerPoints(c[0], c[1], c[2], c[3], RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.001) : null);
        crv.Add(new PolylineCurve(new[] { c[0], c[1], c[2], c[3], c[0] }));
        cov.Add(r.Coverage);
        string ol = "";
        if (cell > 0 && r.Width > 0 && r.Height > 0)
        {
          var U = c[1] - c[0]; var V = c[3] - c[0]; U.Unitize(); V.Unitize();
          var o = OutlineCore.Run(c[0], U, V, r.Width, r.Height, segs[i].Points.GetPoints(), cell, Math.Max(0, close), gap);
          var b = o.Loops.Count > 0 ? Brep.CreatePlanarBreps(o.Loops.Select(x => (Curve)new PolylineCurve(x)), tol) : null;
          var path = new Grasshopper.Kernel.Data.GH_Path(i);
          if (b != null) shape.AddRange(b, path); else shape.EnsurePath(path);
          ol = string.Format(", outline {0} loops, {1:0} % of the rectangle", o.Loops.Count, (b == null ? 0 : b.Sum(x => x.GetArea())) / (r.Width * r.Height) * 100);
        }
        var tr = Enumerable.Range(0, 4).Where(k => r.Trimmed[k]).Select(k => en[k] + "→" + r.Neighbour[k]).ToList();
        info.Add(string.Format("Seg {0}: {1:0.00} × {2:0.00} m, {3}, trimmed {4}, coverage {5:0} %{6}",
          segs[i].Id, r.Width, r.Height, r.Orientation, tr.Count == 0 ? "none" : string.Join(" ", tr), r.Coverage * 100, ol));
      }
      info.Add(Ms);
      DA.SetDataList(0, srf); DA.SetDataList(1, crv); DA.SetDataList(2, cov); DA.SetDataList(3, info); DA.SetDataTree(4, shape);
    }
  }
}
