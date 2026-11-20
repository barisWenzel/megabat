using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using GH_IO.Serialization;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using Megabat.Core;

namespace Megabat.Types
{
  // Photo: oriented image (camera plane, intrinsics, rectangle on the image board, file) in one package
  public class PhotoGoo : GH_Goo<PhotoCam>, IGH_PreviewData
  {
    public PhotoGoo() { }
    public PhotoGoo(PhotoCam p) { m_value = p; }

    public override bool IsValid => m_value != null && m_value.Cam.IsValid && m_value.W > 0;
    public override string TypeName => "Photo";
    public override string TypeDescription => "Oriented photo: camera plane, intrinsics, rectangle on the image board and image file";
    public override string ToString() => m_value == null ? "Empty photo" : string.Format("Photo {0} ({1} × {2} px)", m_value.Name, m_value.W, m_value.H);
    public override IGH_Goo Duplicate() => new PhotoGoo(m_value?.Clone());
    public override object ScriptVariable() => m_value;

    public override bool CastFrom(object source)
    {
      if (source is PhotoGoo g) { m_value = g.m_value; return true; }
      if (source is PhotoCam p) { m_value = p; return true; }
      return false;
    }

    public override bool CastTo<Q>(ref Q target)
    {
      if (m_value == null) return false;
      if (typeof(Q).IsAssignableFrom(typeof(PhotoCam))) { target = (Q)(object)m_value; return true; }
      if (typeof(Q).IsAssignableFrom(typeof(Plane))) { target = (Q)(object)m_value.Cam; return true; }
      if (typeof(Q).IsAssignableFrom(typeof(GH_Plane))) { target = (Q)(object)new GH_Plane(m_value.Cam); return true; }
      if (typeof(Q).IsAssignableFrom(typeof(Rectangle3d))) { target = (Q)(object)m_value.Frame; return true; }
      if (typeof(Q).IsAssignableFrom(typeof(GH_Rectangle))) { target = (Q)(object)new GH_Rectangle(m_value.Frame); return true; }
      if (typeof(Q).IsAssignableFrom(typeof(GH_String))) { target = (Q)(object)new GH_String(m_value.Name); return true; }
      return false;
    }

    // ---- preview: image on the board, outline and name ----
    static readonly Dictionary<string, DisplayMaterial> Mats = new Dictionary<string, DisplayMaterial>();
    Mesh _quad;

    public BoundingBox ClippingBox => m_value == null ? BoundingBox.Empty : m_value.Frame.BoundingBox;

    public void DrawViewportMeshes(GH_PreviewMeshArgs args)
    {
      if (m_value?.Path == null || !m_value.Frame.IsValid) return;
      if (!Mats.TryGetValue(m_value.Path, out var mat))
      {
        mat = new DisplayMaterial(Color.White);
        mat.SetBitmapTexture(m_value.Path, true);
        Mats[m_value.Path] = mat;
      }
      if (_quad == null)
      {
        var b = m_value.Frame.BoundingBox; double z = b.Min.Z;
        _quad = new Mesh();
        _quad.Vertices.Add(b.Min.X, b.Min.Y, z); _quad.Vertices.Add(b.Max.X, b.Min.Y, z); _quad.Vertices.Add(b.Max.X, b.Max.Y, z); _quad.Vertices.Add(b.Min.X, b.Max.Y, z);
        _quad.TextureCoordinates.Add(0, 0); _quad.TextureCoordinates.Add(1, 0); _quad.TextureCoordinates.Add(1, 1); _quad.TextureCoordinates.Add(0, 1);
        _quad.Faces.AddFace(0, 1, 2, 3); _quad.Normals.ComputeNormals();
      }
      args.Pipeline.DrawMeshShaded(_quad, mat);
    }

    public void DrawViewportWires(GH_PreviewWireArgs args)
    {
      if (m_value == null || !m_value.Frame.IsValid) return;
      args.Pipeline.DrawPolyline(m_value.Frame.ToPolyline(), args.Color, 1);
      args.Pipeline.Draw2dText(m_value.Name ?? "", args.Color, m_value.Frame.Corner(0) + new Vector3d(0, -0.03 * m_value.Frame.Height, 0), false, 14);
    }

    // ---- stored in the .gh when internalised ----
    public override bool Write(GH_IWriter w)
    {
      if (m_value == null) return true;
      var p = m_value;
      w.SetString("Name", p.Name ?? ""); w.SetString("Path", p.Path ?? "");
      w.SetPlane("Cam", new GH_IO.Types.GH_Plane(p.Cam.OriginX, p.Cam.OriginY, p.Cam.OriginZ, p.Cam.XAxis.X, p.Cam.XAxis.Y, p.Cam.XAxis.Z, p.Cam.YAxis.X, p.Cam.YAxis.Y, p.Cam.YAxis.Z));
      w.SetString("K", p.K);
      var b = p.Frame.BoundingBox;
      w.SetDoubleArray("Frame", new[] { b.Min.X, b.Min.Y, b.Max.X, b.Max.Y, b.Min.Z });
      return true;
    }
    public override bool Read(GH_IReader r)
    {
      if (!r.ItemExists("K")) { m_value = null; return true; }
      var pl = r.GetPlane("Cam");
      var cam = new Plane(new Point3d(pl.Origin.x, pl.Origin.y, pl.Origin.z), new Vector3d(pl.XAxis.x, pl.XAxis.y, pl.XAxis.z), new Vector3d(pl.YAxis.x, pl.YAxis.y, pl.YAxis.z));
      var f = r.GetDoubleArray("Frame");
      var fr = new Rectangle3d(Plane.WorldXY, new Point3d(f[0], f[1], f[4]), new Point3d(f[2], f[3], f[4]));
      m_value = PhotoCam.From(cam, r.GetString("K"), fr);
      if (m_value != null) { m_value.Name = r.GetString("Name"); var path = r.GetString("Path"); m_value.Path = path.Length == 0 ? null : path; }
      return true;
    }
  }

  // Photo parameter
  public class PhotoParam : GH_Param<PhotoGoo>, IGH_PreviewObject
  {
    public PhotoParam() : base(new GH_InstanceDescription("Photo", "Photo",
      "Oriented photos from ImportColmap", Tab.Name, Tab.Photo), GH_ParamAccess.list) { }

    public override Guid ComponentGuid => new Guid("1b7e1a70-2f45-4c55-9e1f-0c2b77a4d103");
    public override GH_Exposure Exposure => GH_Exposure.hidden;
    protected override Bitmap Icon => Icons.For("P", Icons.Photo);

    bool _hidden;
    public bool Hidden { get => _hidden; set => _hidden = value; }
    public override bool Write(GH_IWriter writer) { if (_hidden) writer.SetBoolean("MegabatHidden", true); return base.Write(writer); }
    public override bool Read(GH_IReader reader) { _hidden = reader.ItemExists("MegabatHidden") ? reader.GetBoolean("MegabatHidden") : reader.ItemExists("Hidden") && reader.GetBoolean("Hidden"); return base.Read(reader); }
    public bool IsPreviewCapable => true;
    public BoundingBox ClippingBox => Preview_ComputeClippingBox();
    public void DrawViewportWires(IGH_PreviewArgs args) => Preview_DrawWires(args);
    public void DrawViewportMeshes(IGH_PreviewArgs args) => Preview_DrawMeshes(args);
  }
}
