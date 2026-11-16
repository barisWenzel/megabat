using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using GH_IO.Serialization;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Megabat.Core;

namespace Megabat.Types
{
  // Point cloud that is not copied when passed on and draws itself thinned
  public class CloudGoo : GH_GeometricGoo<PointCloud>, IGH_PreviewData, IGH_BakeAwareData
  {
    PointCloud _thin; int _thinMax = -1;
    Guid _ref = Guid.Empty;
    string _file;   // set when the cloud was loaded from the file of a proxy
    BoundingBox _box = BoundingBox.Unset;

    public CloudGoo() { }
    public CloudGoo(PointCloud pc) { m_value = pc; }
    public CloudGoo(Guid id) { _ref = id; }
    public CloudGoo(PointCloud pc, string file) { m_value = pc; _file = file; }

    // The base class does not store the reference itself
    public override Guid ReferenceID { get => _ref; set => _ref = value; }
    public override bool IsReferencedGeometry => _ref != Guid.Empty;

    public override bool IsValid => m_value != null;
    public override string IsValidWhyNot => m_value == null ? "No point cloud" : string.Empty;
    public override string TypeName => "Cloud";
    public override string TypeDescription => "Point cloud (Megabat), passed on without copying";
    public override string ToString() => m_value == null ? "Empty cloud" :
      string.Format("Cloud ({0:N0} points{1})", m_value.Count, _file != null ? ", full cloud from " + System.IO.Path.GetFileName(_file) : "");

    // Shallow copy: same cloud, new wrapper only
    public override IGH_Goo Duplicate() => new CloudGoo(m_value) { ReferenceID = ReferenceID, _file = _file };
    public override IGH_GeometricGoo DuplicateGeometry() => new CloudGoo(m_value?.Duplicate() as PointCloud);

    public override BoundingBox Boundingbox
    {
      get
      {
        if (m_value == null) return BoundingBox.Empty;
        if (!_box.IsValid) _box = m_value.GetBoundingBox(true);
        return _box;
      }
    }
    public override BoundingBox GetBoundingBox(Transform xform) => m_value == null ? BoundingBox.Empty : m_value.GetBoundingBox(xform);

    public override IGH_GeometricGoo Transform(Transform xform)
    {
      if (m_value == null) return null;
      var c = (PointCloud)m_value.Duplicate();
      c.Transform(xform);
      return new CloudGoo(c);
    }

    public override IGH_GeometricGoo Morph(SpaceMorph xmorph)
    {
      if (m_value == null) return null;
      var c = (PointCloud)m_value.Duplicate();
      xmorph.Morph(c);
      return new CloudGoo(c);
    }

    public override object ScriptVariable() => m_value;

    // Reference to a Rhino object: geometry is loaded, not copied
    public override bool IsGeometryLoaded => m_value != null;
    public override bool LoadGeometry(RhinoDoc doc)
    {
      if (doc == null || ReferenceID == Guid.Empty) return false;
      var obj = doc.Objects.FindId(ReferenceID);
      if (!(obj?.Geometry is PointCloud pc)) return false;
      _thin = null; _box = BoundingBox.Unset; _file = null;
      // Proxy from MegabatImport/MegabatProxy: compute with the full cloud from its file
      var src = CloudFile.SourceOf(obj.Attributes, doc.Path);
      if (src != null)
      {
        try { m_value = CloudFile.Load(src, doc.ModelUnitSystem); _file = src; return true; }
        catch { }
      }
      m_value = pc;
      return true;
    }
    public override void ClearCaches()
    {
      if (IsReferencedGeometry) m_value = null;
      _thin = null; _box = BoundingBox.Unset;
    }

    public override bool CastFrom(object source)
    {
      switch (source)
      {
        case CloudGoo g: m_value = g.m_value; ReferenceID = g.ReferenceID; return true;
        case PointCloud pc: m_value = pc; return true;
        case GH_PointCloud gp: m_value = gp.Value; ReferenceID = gp.ReferenceID; return m_value != null;
        case GH_ObjectWrapper w when w.Value is PointCloud wp: m_value = wp; return true;
        case Guid id: ReferenceID = id; return LoadGeometry(RhinoDoc.ActiveDoc);
        case GH_Guid gg: ReferenceID = gg.Value; return LoadGeometry(RhinoDoc.ActiveDoc);
        case SegmentGoo sg when sg.Value?.Points != null: m_value = sg.Value.Points; return true;
        case IGH_Goo goo when goo.ScriptVariable() is PointCloud sp: m_value = sp; return true;
      }
      return false;
    }

    public override bool CastTo<Q>(ref Q target)
    {
      if (typeof(Q).IsAssignableFrom(typeof(PointCloud))) { target = (Q)(object)m_value; return m_value != null; }
      if (typeof(Q).IsAssignableFrom(typeof(GH_PointCloud))) { target = (Q)(object)new GH_PointCloud(m_value); return m_value != null; }
      if (typeof(Q).IsAssignableFrom(typeof(GH_ObjectWrapper))) { target = (Q)(object)new GH_ObjectWrapper(m_value); return true; }
      return false;
    }

    // Preview: thinned (PreviewGuard), with point colours if present
    public BoundingBox ClippingBox => Boundingbox;
    public void DrawViewportWires(GH_PreviewWireArgs args)
    {
      if (m_value == null || m_value.Count == 0) return;
      int max = Display.PreviewGuard.Enabled ? Display.PreviewGuard.MaxPts : int.MaxValue;
      if (_thin == null || _thinMax != max)
      {
        _thin = CloudUtil.Thin(m_value, max); _thinMax = max;
        // Never draw the geometry instance of the Rhino object itself: Rhino shares its display
        // cache, so the cloud turned yellow whenever something was selected in Rhino.
        if (IsReferencedGeometry && ReferenceEquals(_thin, m_value)) _thin = (PointCloud)m_value.Duplicate();
        if (m_value.Count > max) Display.PreviewGuard.Notify();
      }
      // Always pass a colour: it is only used if the points have none. Without it Rhino may take
      // the last display colour (e.g. selection yellow).
      args.Pipeline.DrawPointCloud(_thin, 2, args.Color);
    }
    public void DrawViewportMeshes(GH_PreviewMeshArgs args) { }

    public bool BakeGeometry(RhinoDoc doc, ObjectAttributes att, out Guid obj_guid)
    {
      obj_guid = Guid.Empty;
      if (m_value == null) return false;
      obj_guid = doc.Objects.AddPointCloud(m_value, att);
      return obj_guid != Guid.Empty;
    }

    // References store only the ID; internalised clouds are stored completely (file size!)
    public override bool Write(GH_IWriter writer)
    {
      writer.SetGuid("RefID", ReferenceID);
      if (ReferenceID == Guid.Empty && m_value != null)
        writer.SetByteArray("ON", GH_Convert.CommonObjectToByteArray(m_value));
      return true;
    }

    public override bool Read(GH_IReader reader)
    {
      if (reader.ItemExists("RefID")) ReferenceID = reader.GetGuid("RefID");
      if (reader.ItemExists("ON")) m_value = GH_Convert.ByteArrayToCommonObject<PointCloud>(reader.GetByteArray("ON"));
      return true;
    }
  }
}
