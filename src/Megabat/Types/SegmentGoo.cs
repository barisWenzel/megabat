using System;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using GH_IO.Serialization;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Megabat.Core;

namespace Megabat.Types
{
  // Segment: plane + points + type in one package
  public class SegmentGoo : GH_Goo<Segment>, IGH_PreviewData, IGH_BakeAwareData
  {
    PointCloud _thin;

    public SegmentGoo() { }
    public SegmentGoo(Segment s) { m_value = s; }

    public override bool IsValid => m_value != null && m_value.Plane.IsValid;
    public override string TypeName => "Segment";
    public override string TypeDescription => "Detected surface: plane, points and type (horizontal, vertical, inclined)";
    public override string ToString() => m_value == null ? "Empty segment" : m_value.ToString();
    public override IGH_Goo Duplicate() => new SegmentGoo(m_value?.Clone());
    public override object ScriptVariable() => m_value;

    public override bool CastFrom(object source)
    {
      if (source is SegmentGoo g) { m_value = g.m_value; return true; }
      if (source is Segment s) { m_value = s; return true; }
      return false;
    }

    public override bool CastTo<Q>(ref Q target)
    {
      if (m_value == null) return false;
      if (typeof(Q).IsAssignableFrom(typeof(Segment))) { target = (Q)(object)m_value; return true; }
      if (typeof(Q).IsAssignableFrom(typeof(Plane))) { target = (Q)(object)m_value.Plane; return true; }
      if (typeof(Q).IsAssignableFrom(typeof(GH_Plane))) { target = (Q)(object)new GH_Plane(m_value.Plane); return true; }
      if (typeof(Q).IsAssignableFrom(typeof(PointCloud))) { target = (Q)(object)m_value.Points; return m_value.Points != null; }
      if (typeof(Q).IsAssignableFrom(typeof(CloudGoo))) { target = (Q)(object)new CloudGoo(m_value.Points); return m_value.Points != null; }
      if (typeof(Q).IsAssignableFrom(typeof(GH_Integer))) { target = (Q)(object)new GH_Integer((int)m_value.Type); return true; }
      return false;
    }

    public BoundingBox ClippingBox => m_value?.Points == null ? BoundingBox.Empty : m_value.Points.GetBoundingBox(true);
    public void DrawViewportWires(GH_PreviewWireArgs args)
    {
      if (m_value?.Points == null || m_value.Points.Count == 0) return;
      if (_thin == null) _thin = CloudUtil.Thin(m_value.Points, 50000);
      if (_thin.ContainsColors) args.Pipeline.DrawPointCloud(_thin, 2);
      else args.Pipeline.DrawPointCloud(_thin, 2, args.Color);
    }
    public void DrawViewportMeshes(GH_PreviewMeshArgs args) { }

    public bool BakeGeometry(RhinoDoc doc, ObjectAttributes att, out Guid obj_guid)
    {
      obj_guid = Guid.Empty;
      if (m_value?.Points == null) return false;
      var a = att.Duplicate();
      a.Name = m_value.ToString();
      obj_guid = doc.Objects.AddPointCloud(m_value.Points, a);
      return obj_guid != Guid.Empty;
    }

    // Segments are not stored in the .gh (they are derived from the cloud)
    public override bool Write(GH_IWriter writer) => true;
    public override bool Read(GH_IReader reader) => true;
  }
}
