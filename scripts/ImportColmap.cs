// Megabat test: COLMAP cameras into Rhino, images laid out flat in the XY plane for measuring
// Inputs:  dir (text) folder with cameras.txt and images.txt (export the model as text, ideally after image_undistorter),
//          img (text, optional) folder with the images; default: dir\..\images, dir\images or dir
//          px (double) size of one pixel in document units, default 1 px = 1 mm
//          cols (int) images per row, default 6;  bake (bool) add the images as locked picture frames on Megabat::Photos::<name>
// Outputs: Cam (planes: origin = projection centre, X/Y = image axes u/v, Z = viewing direction), K (text "fx fy cx cy w h"),
//          Frame (rectangle of each image on the board), Name, Info
#region Usings
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;
using Grasshopper;
using Grasshopper.Kernel;
#endregion

public class Script_Instance : GH_ScriptInstance
{
  class Img { public string Name, Path; public Plane Cam; public double Fx, Fy, Cx, Cy, W, H; public Rectangle3d Frame; public Mesh Quad; public DisplayMaterial Mat; }
  List<Img> _imgs = new List<Img>();
  string _key = "";
  BoundingBox _bb = BoundingBox.Empty;

  private void RunScript(object dir, object img, object px, object cols, object bake,
    ref object Cam, ref object K, ref object Frame, ref object Name, ref object Info)
  {
    Component.Message = "ImportColmap";
    if (dir == null) { Info = "dir: folder with cameras.txt and images.txt"; return; }
    string d = dir.ToString().Trim().Trim('"');
    double s = px == null ? RhinoMath.UnitScale(UnitSystem.Millimeters, RhinoDoc.ActiveDoc?.ModelUnitSystem ?? UnitSystem.Meters) : Convert.ToDouble(px);   // default: 1 px = 1 mm
    int nc = cols == null ? 6 : Math.Max(1, Convert.ToInt32(cols));
    string idir = img == null ? null : img.ToString().Trim().Trim('"');
    if (idir == null)
      foreach (var c in new[] { Path.Combine(d, "..", "images"), Path.Combine(d, "images"), d })
        if (Directory.Exists(c)) { idir = Path.GetFullPath(c); break; }

    var notes = new List<string>();
    string key = d + "|" + idir + "|" + s + "|" + nc + "|" + File.GetLastWriteTimeUtc(Path.Combine(d, "images.txt")).Ticks;
    if (key != _key) { Load(d, idir, s, nc, notes); _key = key; }

    if (bake != null && Convert.ToBoolean(bake)) notes.Add(Bake());

    Cam = _imgs.Select(i => i.Cam).ToList();
    K = _imgs.Select(i => string.Format(CultureInfo.InvariantCulture, "{0} {1} {2} {3} {4} {5}", i.Fx, i.Fy, i.Cx, i.Cy, i.W, i.H)).ToList();
    Frame = _imgs.Select(i => i.Frame).ToList();
    Name = _imgs.Select(i => i.Name).ToList();
    Info = string.Format("{0} images, {1} missing image files; 1 px = {2} units{3}", _imgs.Count, _imgs.Count(i => i.Path == null), s,
      notes.Count > 0 ? "; " + string.Join("; ", notes) : "");
  }

  void Load(string d, string idir, double s, int nc, List<string> notes)
  {
    _imgs = new List<Img>(); _bb = BoundingBox.Empty;
    var ci = CultureInfo.InvariantCulture;
    // cameras.txt: CAMERA_ID MODEL WIDTH HEIGHT PARAMS[]
    var cams = new Dictionary<string, double[]>();
    foreach (var l in File.ReadAllLines(Path.Combine(d, "cameras.txt")))
    {
      if (l.StartsWith("#") || l.Trim().Length == 0) continue;
      var t = l.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
      double w = double.Parse(t[2], ci), h = double.Parse(t[3], ci);
      var p = t.Skip(4).Select(x => double.Parse(x, ci)).ToArray();
      string m = t[1];
      double fx, fy, cx, cy;
      if (m == "SIMPLE_PINHOLE" || m == "SIMPLE_RADIAL" || m == "RADIAL") { fx = fy = p[0]; cx = p[1]; cy = p[2]; }
      else { fx = p[0]; fy = p[1]; cx = p[2]; cy = p[3]; }
      if (m != "PINHOLE" && m != "SIMPLE_PINHOLE") notes.Add("camera " + t[0] + " is " + m + ": distortion ignored, use undistorted images");
      cams[t[0]] = new[] { fx, fy, cx, cy, w, h };
    }
    // images.txt: IMAGE_ID QW QX QY QZ TX TY TZ CAMERA_ID NAME, then one line of 2D points
    var lines = File.ReadAllLines(Path.Combine(d, "images.txt")).Where(l => !l.StartsWith("#")).ToList();
    var list = new List<Img>();
    for (int i = 0; i + 1 <= lines.Count; i += 2)
    {
      var t = lines[i].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
      if (t.Length < 10) { i--; continue; }
      double qw = double.Parse(t[1], ci), qx = double.Parse(t[2], ci), qy = double.Parse(t[3], ci), qz = double.Parse(t[4], ci);
      var tv = new Vector3d(double.Parse(t[5], ci), double.Parse(t[6], ci), double.Parse(t[7], ci));
      // rows of R (world -> camera) are the camera axes in world coordinates
      var r0 = new Vector3d(1 - 2 * (qy * qy + qz * qz), 2 * (qx * qy - qz * qw), 2 * (qx * qz + qy * qw));
      var r1 = new Vector3d(2 * (qx * qy + qz * qw), 1 - 2 * (qx * qx + qz * qz), 2 * (qy * qz - qx * qw));
      var r2 = new Vector3d(2 * (qx * qz - qy * qw), 2 * (qy * qz + qx * qw), 1 - 2 * (qx * qx + qy * qy));
      var c = Point3d.Origin - (tv.X * r0 + tv.Y * r1 + tv.Z * r2);   // C = -R^T t
      var k = cams[t[8]];
      string name = string.Join(" ", t.Skip(9));
      string path = idir == null ? null : Path.Combine(idir, name);
      if (path != null && !File.Exists(path)) path = null;
      list.Add(new Img { Name = name, Path = path, Cam = new Plane(c, r0, r1), Fx = k[0], Fy = k[1], Cx = k[2], Cy = k[3], W = k[4], H = k[5] });
    }
    list = list.OrderBy(x => x.Name).ToList();
    // board: rows of nc images, top-left corner of image i at (x0, y0); u to +X, v to -Y
    double gw = list.Count == 0 ? 0 : list.Max(x => x.W) * 1.1 * s, gh = list.Count == 0 ? 0 : list.Max(x => x.H) * 1.15 * s;
    for (int i = 0; i < list.Count; i++)
    {
      var m = list[i];
      double x0 = (i % nc) * gw, y0 = -(i / nc) * gh;
      m.Frame = new Rectangle3d(Plane.WorldXY, new Point3d(x0, y0 - m.H * s, 0), new Point3d(x0 + m.W * s, y0, 0));
      var q = new Mesh();
      q.Vertices.Add(x0, y0 - m.H * s, 0); q.Vertices.Add(x0 + m.W * s, y0 - m.H * s, 0); q.Vertices.Add(x0 + m.W * s, y0, 0); q.Vertices.Add(x0, y0, 0);
      q.TextureCoordinates.Add(0, 0); q.TextureCoordinates.Add(1, 0); q.TextureCoordinates.Add(1, 1); q.TextureCoordinates.Add(0, 1);
      q.Faces.AddFace(0, 1, 2, 3); q.Normals.ComputeNormals();
      m.Quad = q;
      if (m.Path != null) { m.Mat = new DisplayMaterial(System.Drawing.Color.White); m.Mat.SetBitmapTexture(m.Path, true); }
      _bb.Union(m.Frame.BoundingBox);
    }
    _imgs = list;
  }

  string Bake()
  {
    var doc = RhinoDoc.ActiveDoc; if (doc == null) return "no Rhino document";
    int parent = Layer(doc, "Megabat", -1), photos = Layer(doc, "Photos", parent), n = 0;
    foreach (var m in _imgs.Where(x => x.Path != null))
    {
      int li = Layer(doc, Path.GetFileNameWithoutExtension(m.Name), photos);
      var lay = doc.Layers[li];
      if (doc.Objects.FindByLayer(lay)?.Length > 0) continue;   // already baked
      var att = doc.CreateDefaultAttributes(); att.LayerIndex = li; att.Name = m.Name;
      var pl = new Plane(m.Frame.Corner(0), Vector3d.XAxis, Vector3d.YAxis);
      var id = doc.Objects.AddPictureFrame(pl, m.Path, false, m.Frame.Width, m.Frame.Height, true, false);
      var obj = doc.Objects.FindId(id); if (obj != null) { obj.Attributes.LayerIndex = li; obj.Attributes.Name = m.Name; obj.CommitChanges(); }
      lay.IsLocked = true; n++;
    }
    doc.Views.Redraw();
    return n + " images baked to locked layers Megabat::Photos";
  }

  static int Layer(RhinoDoc doc, string name, int parent)
  {
    var pid = parent < 0 ? Guid.Empty : doc.Layers[parent].Id;
    foreach (var l in doc.Layers) if (!l.IsDeleted && l.Name == name && l.ParentLayerId == pid) return l.Index;
    var nl = new Rhino.DocObjects.Layer { Name = name, ParentLayerId = pid };
    return doc.Layers.Add(nl);
  }

  public override BoundingBox ClippingBox => _bb;

  public override void DrawViewportMeshes(IGH_PreviewArgs args)
  {
    foreach (var m in _imgs)
      if (m.Mat != null) args.Display.DrawMeshShaded(m.Quad, m.Mat);
  }

  public override void DrawViewportWires(IGH_PreviewArgs args)
  {
    foreach (var m in _imgs)
    {
      args.Display.DrawPolyline(m.Frame.ToPolyline(), System.Drawing.Color.Black, 1);
      args.Display.Draw2dText(m.Name, System.Drawing.Color.Black, m.Frame.Corner(0) + new Vector3d(0, -0.03 * m.Frame.Height, 0), false, 14);
    }
  }
}
