using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Megabat.Core;
using Megabat.Types;

namespace Megabat.Components
{
  public class ShowComponent : MegabatComponent
  {
    public ShowComponent() : base("Show", "Show",
      "Custom preview with line weight or point size and colour, for curves, lines, points, Breps, meshes, clouds and segments.", Tab.Display) { }
    public override Guid ComponentGuid => new Guid("a3c9f0d2-11e4-4b6a-8a01-6d2f5e000041");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override string IconText => "Sh";
    protected override Color IconColor => Icons.Display;

    List<object> _geo = new List<object>();
    BoundingBox _box = BoundingBox.Empty;
    Color _col = Color.Empty;
    int _w = 2;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
      p.AddGenericParameter("G", "G", "Geometry: curves, lines, points, Breps, meshes, clouds, segments", GH_ParamAccess.list);
      int c = p.AddColourParameter("col", "col", "Optional: colour; if empty, the point colours of the cloud or black", GH_ParamAccess.item);
      p[c].Optional = true;
      p.AddIntegerParameter("weight", "weight", "Line weight or point size in pixels", GH_ParamAccess.item, 2);
      p.AddIntegerParameter("maxPts", "maxPts", "Clouds are thinned to this many points in total for display", GH_ParamAccess.item, 1000000);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
      p.AddTextParameter("Info", "Info", "Number of drawn objects and points", GH_ParamAccess.item);
    }

    protected override void BeforeSolveInstance()
    {
      _geo = new List<object>(); _box = BoundingBox.Empty;
      base.BeforeSolveInstance();
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      var G = new List<IGH_Goo>(); Color col = Color.Empty; int weight = 2, maxPts = 1000000;
      DA.GetDataList(0, G);
      bool hasCol = DA.GetData(1, ref col);
      DA.GetData(2, ref weight); DA.GetData(3, ref maxPts);
      _col = hasCol ? col : Color.Empty;
      _w = Math.Max(1, weight);

      var clouds = new List<PointCloud>(); long total = 0;
      foreach (var goo in G)
      {
        if (goo == null) continue;
        object v = goo is SegmentGoo sg ? sg.Value?.Points : goo.ScriptVariable();
        if (v is GH_ObjectWrapper w) v = w.Value;
        if (v == null) continue;
        if (v is PointCloud pc) { clouds.Add(pc); total += pc.Count; continue; }
        _geo.Add(v);
        if (v is GeometryBase gb) _box.Union(gb.GetBoundingBox(false));
        else if (v is Line ln) _box.Union(ln.BoundingBox);
        else if (v is Point3d pt) _box.Union(pt);
        else if (v is Circle ci) _box.Union(ci.BoundingBox);
      }
      int max = maxPts > 0 ? maxPts : 1000000;
      long shown = 0;
      foreach (var pc in clouds)
      {
        int share = total > max ? (int)Math.Max(1, (long)max * pc.Count / total) : pc.Count;
        var t = CloudUtil.Thin(pc, share);
        // With a given colour: copy without point colours, otherwise the point colours win when drawing
        if (hasCol && t.ContainsColors) { var u = new PointCloud(); u.AddRange(t.GetPoints()); t = u; }
        _geo.Add(t); shown += t.Count;
        _box.Union(pc.GetBoundingBox(false));
      }
      DA.SetData(0, string.Format("{0} objects, {1} of them clouds with {2:N0} drawn points (original {3:N0})", _geo.Count, clouds.Count, shown, total));
    }

    public override BoundingBox ClippingBox => _box;

    public override void DrawViewportWires(IGH_PreviewArgs args)
    {
      var c = _col.IsEmpty ? Color.Black : _col;
      foreach (var g in _geo)
      {
        if (g is PointCloud pc)
        {
          if (_col.IsEmpty && pc.ContainsColors) args.Display.DrawPointCloud(pc, _w);
          else args.Display.DrawPointCloud(pc, _w, c);
        }
        else if (g is Curve cv) args.Display.DrawCurve(cv, c, _w);
        else if (g is Line ln) args.Display.DrawLine(ln, c, _w);
        else if (g is Circle ci) args.Display.DrawCircle(ci, c, _w);
        else if (g is Point3d pt) args.Display.DrawPoint(pt, Rhino.Display.PointStyle.RoundSimple, _w + 2, c);
        else if (g is Brep b) foreach (var e in b.Edges) args.Display.DrawCurve(e, c, _w);
        else if (g is Mesh m) args.Display.DrawMeshWires(m, c, _w);
      }
    }
    public override void DrawViewportMeshes(IGH_PreviewArgs args) { }
  }
}
