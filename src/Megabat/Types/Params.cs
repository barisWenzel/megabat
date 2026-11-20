using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using GH_IO.Serialization;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace Megabat.Types
{
  // Cloud parameter: references point clouds from Rhino, draws thinned, never copies
  public class CloudParam : GH_PersistentGeometryParam<CloudGoo>, IGH_PreviewObject, IGH_BakeAwareObject
  {
    // Display: 0 = as set in the PreviewGuard menu, 1 = thinned, 2 = level of detail
    public int DisplayMode { get; set; }

    public override void AppendAdditionalMenuItems(System.Windows.Forms.ToolStripDropDown menu)
    {
      base.AppendAdditionalMenuItems(menu);
      Menu_AppendSeparator(menu);
      string[] names = { "Display: as set in PreviewGuard", "Display: thinned", "Display: level of detail (octree)" };
      for (int i = 0; i < 3; i++)
      {
        int m = i;
        Menu_AppendItem(menu, names[i], (s, e) => { RecordUndoEvent("Cloud display"); DisplayMode = m; Rhino.RhinoDoc.ActiveDoc?.Views.Redraw(); }, true, DisplayMode == i);
      }
    }

    public CloudParam() : base(new GH_InstanceDescription("Cloud", "Cloud",
      "Point cloud for Megabat. Right click: set point cloud from Rhino. Passed on without copying and displayed thinned.",
      Tab.Name, Tab.Cloud)) { }

    public override Guid ComponentGuid => new Guid("1b7e1a70-2f45-4c55-9e1f-0c2b77a4d101");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => Icons.For("C", Icons.Cloud);

    protected override GH_GetterResult Prompt_Singular(ref CloudGoo value)
    {
      var go = new Rhino.Input.Custom.GetObject();
      go.SetCommandPrompt("Select point cloud");
      go.GeometryFilter = ObjectType.PointSet;
      if (go.Get() != Rhino.Input.GetResult.Object) return GH_GetterResult.cancel;
      value = new CloudGoo(go.Object(0).ObjectId);
      value.LoadGeometry(RhinoDoc.ActiveDoc);
      return GH_GetterResult.success;
    }

    protected override GH_GetterResult Prompt_Plural(ref List<CloudGoo> values)
    {
      var go = new Rhino.Input.Custom.GetObject();
      go.SetCommandPrompt("Select point clouds");
      go.GeometryFilter = ObjectType.PointSet;
      if (go.GetMultiple(1, 0) != Rhino.Input.GetResult.Object) return GH_GetterResult.cancel;
      values = new List<CloudGoo>();
      for (int i = 0; i < go.ObjectCount; i++)
      {
        var g = new CloudGoo(go.Object(i).ObjectId);
        g.LoadGeometry(RhinoDoc.ActiveDoc);
        values.Add(g);
      }
      return GH_GetterResult.success;
    }

    bool _hidden;
    public bool Hidden { get => _hidden; set => _hidden = value; }
    public override bool Write(GH_IWriter writer) { writer.SetBoolean("Hidden", _hidden); writer.SetInt32("DisplayMode", DisplayMode); return base.Write(writer); }
    public override bool Read(GH_IReader reader)
    {
      if (reader.ItemExists("Hidden")) _hidden = reader.GetBoolean("Hidden");
      if (reader.ItemExists("DisplayMode")) DisplayMode = reader.GetInt32("DisplayMode");
      return base.Read(reader);
    }
    public bool IsPreviewCapable => true;
    public BoundingBox ClippingBox => Preview_ComputeClippingBox();
    public void DrawViewportWires(IGH_PreviewArgs args) => Preview_DrawWires(args);
    public void DrawViewportMeshes(IGH_PreviewArgs args) { }

    public bool IsBakeCapable => !m_data.IsEmpty;
    public void BakeGeometry(RhinoDoc doc, List<Guid> obj_ids) => BakeGeometry(doc, null, obj_ids);
    public void BakeGeometry(RhinoDoc doc, ObjectAttributes att, List<Guid> obj_ids)
    {
      if (att == null) att = doc.CreateDefaultAttributes();
      foreach (var g in m_data.AllData(true))
        if (g is IGH_BakeAwareData b && b.BakeGeometry(doc, att, out Guid id)) obj_ids.Add(id);
    }
  }

  // Segment parameter
  public class SegmentParam : GH_Param<SegmentGoo>, IGH_PreviewObject, IGH_BakeAwareObject
  {
    public SegmentParam() : base(new GH_InstanceDescription("Segment", "Seg",
      "Detected surfaces (plane, points, type) from Segmentize or Classify", Tab.Name, Tab.Segments), GH_ParamAccess.list) { }

    public override Guid ComponentGuid => new Guid("1b7e1a70-2f45-4c55-9e1f-0c2b77a4d102");
    public override GH_Exposure Exposure => GH_Exposure.hidden;
    protected override Bitmap Icon => Icons.For("S", Icons.Segments);

    bool _hidden;
    public bool Hidden { get => _hidden; set => _hidden = value; }
    public override bool Write(GH_IWriter writer) { writer.SetBoolean("Hidden", _hidden); return base.Write(writer); }
    public override bool Read(GH_IReader reader) { if (reader.ItemExists("Hidden")) _hidden = reader.GetBoolean("Hidden"); return base.Read(reader); }
    public bool IsPreviewCapable => true;
    public BoundingBox ClippingBox => Preview_ComputeClippingBox();
    public void DrawViewportWires(IGH_PreviewArgs args) => Preview_DrawWires(args);
    public void DrawViewportMeshes(IGH_PreviewArgs args) { }

    public bool IsBakeCapable => !m_data.IsEmpty;
    public void BakeGeometry(RhinoDoc doc, List<Guid> obj_ids) => BakeGeometry(doc, null, obj_ids);
    public void BakeGeometry(RhinoDoc doc, ObjectAttributes att, List<Guid> obj_ids)
    {
      if (att == null) att = doc.CreateDefaultAttributes();
      foreach (var g in m_data.AllData(true))
        if (g is IGH_BakeAwareData b && b.BakeGeometry(doc, att, out Guid id)) obj_ids.Add(id);
    }
  }
}
