using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using Megabat.Core;
using Megabat.Types;

namespace Megabat.Components
{
  // Shared helpers for the photo components: reading photos and Megabat layers
  static class Photo
  {
    public static List<PhotoCam> ReadPhotos(IGH_DataAccess DA, int i)
    {
      var g = new List<PhotoGoo>(); DA.GetDataList(i, g);
      return g.Where(x => x?.Value != null).Select(x => x.Value).ToList();
    }

    public static int Layer(RhinoDoc doc, string name, int parent, bool create = true)
    {
      var pid = parent < 0 ? Guid.Empty : doc.Layers[parent].Id;
      foreach (var l in doc.Layers) if (!l.IsDeleted && l.Name == name && l.ParentLayerId == pid) return l.Index;
      return create ? doc.Layers.Add(new Rhino.DocObjects.Layer { Name = name, ParentLayerId = pid }) : -1;
    }
  }

  // Base of the photo components: Photos input
  public abstract class PhotoComponent : MegabatComponent
  {
    protected PhotoComponent(string name, string nick, string description, string sub) : base(name, nick, description, sub) { }

    protected static void AddPhotos(GH_InputParamManager p, bool optional)
    {
      int i = p.AddParameter(new PhotoParam(), "Photos", "Photos", "Oriented photos from ImportColmap", GH_ParamAccess.list);
      p[i].Optional = optional;
    }
  }

  public class ImportColmapComponent : PhotoComponent
  {
    public ImportColmapComponent() : base("ImportColmap", "Colmap",
      "Reads a COLMAP model exported as text (cameras.txt, images.txt) and lays the images out flat in the XY plane for measuring. Use the undistorted model (COLMAP › Dense reconstruction › Undistortion).", Tab.Photo) { }
    public override Guid ComponentGuid => new Guid("a3c9f0d2-11e4-4b6a-8a01-6d2f5e000051");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override string IconText => "IC";
    protected override Color IconColor => Icons.Photo;

    List<PhotoCam> _imgs = new List<PhotoCam>();
    string _key = "";

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
      p.AddTextParameter("dir", "dir", "Folder with cameras.txt and images.txt", GH_ParamAccess.item);
      int a = p.AddTextParameter("img", "img", "Folder with the images; default dir\\..\\images, dir\\images or dir", GH_ParamAccess.item);
      int b = p.AddNumberParameter("px", "px", "Size of one pixel in document units; default 1 mm", GH_ParamAccess.item);
      p.AddIntegerParameter("cols", "cols", "Images per row", GH_ParamAccess.item, 6);
      p.AddBooleanParameter("bake", "bake", "true = add the images as locked picture frames on Megabat::Photos::<name>", GH_ParamAccess.item, false);
      Optional(p, a, b);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
      p.AddParameter(new PhotoParam(), "Photos", "Photos", "Oriented photos: camera plane, intrinsics, rectangle on the board, image file (COLMAP units); Deconstruct Photo for the parts", GH_ParamAccess.list);
      p.AddTextParameter("Info", "Info", "Number of images, missing files, scale, notes", GH_ParamAccess.item);
    }
    protected override void SolveInstance(IGH_DataAccess DA)
    {
      string dir = null, img = null; double px = double.NaN; int cols = 6; bool bake = false;
      if (!DA.GetData(0, ref dir)) return;
      DA.GetData(1, ref img); DA.GetData(2, ref px); DA.GetData(3, ref cols); DA.GetData(4, ref bake);
      dir = dir.Trim().Trim('"');
      if (!File.Exists(Path.Combine(dir, "images.txt")) || !File.Exists(Path.Combine(dir, "cameras.txt")))
      { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "cameras.txt or images.txt not found in dir; export the model as text"); return; }
      double s = double.IsNaN(px) || px <= 0 ? RhinoMath.UnitScale(UnitSystem.Millimeters, RhinoDoc.ActiveDoc?.ModelUnitSystem ?? UnitSystem.Meters) : px;
      cols = Math.Max(1, cols);
      string idir = string.IsNullOrWhiteSpace(img) ? null : img.Trim().Trim('"');
      if (idir == null)
        foreach (var c in new[] { Path.Combine(dir, "..", "images"), Path.Combine(dir, "images"), dir })
          if (Directory.Exists(c)) { idir = Path.GetFullPath(c); break; }

      var notes = new List<string>();
      string key = dir + "|" + idir + "|" + s + "|" + cols + "|" + File.GetLastWriteTimeUtc(Path.Combine(dir, "images.txt")).Ticks;
      if (key != _key)
      {
        _imgs = PhotoCore.LoadColmap(dir, idir, notes);
        PhotoCore.Layout(_imgs, s, cols);
        _key = key;
      }
      if (bake) notes.Add(Bake());

      DA.SetDataList(0, _imgs.Select(i => new PhotoGoo(i)));
      DA.SetData(1, string.Format("{0} images, {1} missing image files; 1 px = {2} units{3}; {4}", _imgs.Count, _imgs.Count(i => i.Path == null), s,
        notes.Count > 0 ? "; " + string.Join("; ", notes.Distinct()) : "", Ms));
    }

    string Bake()
    {
      var doc = RhinoDoc.ActiveDoc; if (doc == null) return "no Rhino document";
      int parent = Photo.Layer(doc, "Megabat", -1), photos = Photo.Layer(doc, "Photos", parent), n = 0;
      foreach (var m in _imgs.Where(x => x.Path != null))
      {
        int li = Photo.Layer(doc, Path.GetFileNameWithoutExtension(m.Name), photos);
        var lay = doc.Layers[li];
        if (doc.Objects.FindByLayer(lay)?.Length > 0) continue;   // already baked
        var pl = new Plane(m.Frame.Corner(0), Vector3d.XAxis, Vector3d.YAxis);
        var id = doc.Objects.AddPictureFrame(pl, m.Path, false, m.Frame.Width, m.Frame.Height, true, false);
        var obj = doc.Objects.FindId(id); if (obj != null) { obj.Attributes.LayerIndex = li; obj.Attributes.Name = m.Name; obj.CommitChanges(); }
        lay.IsLocked = true; n++;
      }
      doc.Views.Redraw();
      return n + " images baked to locked layers Megabat::Photos";
    }

  }

  public class MeasureComponent : PhotoComponent
  {
    public MeasureComponent() : base("Measure", "Measure",
      "Measure image points as Rhino points. Add: Grasshopper is minimised, click the same feature in 2 or 3 images, Enter. The clicks become points on the layer Megabat::Measure named P001, P002 ...; they can be moved, copied and deleted in Rhino. With Photos connected: epipolar lines after the first click, predicted position after the second, snapping (option Snap).", Tab.Photo) { }
    public override Guid ComponentGuid => new Guid("a3c9f0d2-11e4-4b6a-8a01-6d2f5e000052");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override string IconText => "MP";
    protected override Color IconColor => Icons.Photo;

    const string Parent = "Megabat", Child = "Measure";
    bool _add0, _undo0, _clear0, _busy, _snap = true, _pending, _hooked;
    List<PhotoCam> _cams = new List<PhotoCam>();
    List<(string name, List<Point3d> pts)> _groups = new List<(string, List<Point3d>)>();

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
      p.AddBooleanParameter("Add", "Add", "Button: measure a new point", GH_ParamAccess.item, false);
      p.AddBooleanParameter("Undo", "Undo", "Button: delete the last point (all its clicks)", GH_ParamAccess.item, false);
      p.AddBooleanParameter("Clear", "Clear", "Button: delete all measure points", GH_ParamAccess.item, false);
      AddPhotos(p, true);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
      p.AddPointParameter("P", "P", "Clicks on the image board, one branch per point", GH_ParamAccess.tree);
      p.AddTextParameter("Name", "Name", "Point name per branch", GH_ParamAccess.list);
      p.AddTextParameter("Info", "Info", "Number of points and clicks", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      var doc = RhinoDoc.ActiveDoc; if (doc == null) return;
      bool a = false, u = false, c = false;
      DA.GetData(0, ref a); DA.GetData(1, ref u); DA.GetData(2, ref c);
      _cams = Photo.ReadPhotos(DA, 3);
      Hook(true);
      if (a && !_add0 && !_busy) { _busy = true; RhinoApp.Idle += Pick; }
      if (u && !_undo0) { var g = Groups(doc); if (g.Count > 0) DeleteNames(doc, new[] { g.Last().name }); }
      if (c && !_clear0) DeleteNames(doc, Groups(doc).Select(g => g.name));
      _add0 = a; _undo0 = u; _clear0 = c;

      _groups = Groups(doc);
      var tree = new DataTree<Point3d>();
      for (int i = 0; i < _groups.Count; i++) tree.AddRange(_groups[i].pts, new GH_Path(i));
      DA.SetDataTree(0, tree);
      DA.SetDataList(1, _groups.Select(g => g.name));
      DA.SetData(2, string.Format("{0} points, {1} clicks, {2} with only one click{3}{4}", _groups.Count, _groups.Sum(g => g.pts.Count),
        _groups.Count(g => g.pts.Count < 2), _busy ? " (picking ...)" : "", _cams.Count > 0 ? ", " + _cams.Count + " cameras for epipolar lines" : ""));
    }

    public override void RemovedFromDocument(GH_Document document) { Hook(false); base.RemovedFromDocument(document); }
    public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
    {
      if (context == GH_DocumentContext.Close || context == GH_DocumentContext.Unloaded) Hook(false);
      base.DocumentContextChanged(document, context);
    }

    // ---- layer and points ----
    static int LayerIndex(RhinoDoc doc, bool create)
    {
      int p = Photo.Layer(doc, Parent, -1, create); if (p < 0) return -1;
      return Photo.Layer(doc, Child, p, create);
    }

    static ObjectEnumeratorSettings Filter(int li) => new ObjectEnumeratorSettings { LayerIndexFilter = li, DeletedObjects = false, HiddenObjects = true, LockedObjects = true };

    static List<(string name, List<Point3d> pts)> Groups(RhinoDoc doc)
    {
      var res = new List<(string, List<Point3d>)>();
      int li = LayerIndex(doc, false); if (li < 0) return res;
      foreach (var g in doc.Objects.GetObjectList(Filter(li)).Where(o => o.Geometry is Rhino.Geometry.Point && !string.IsNullOrEmpty(o.Attributes.Name))
                                    .GroupBy(o => o.Attributes.Name).OrderBy(g => g.Key))
        res.Add((g.Key, g.Select(o => ((Rhino.Geometry.Point)o.Geometry).Location).ToList()));
      return res;
    }

    static void DeleteNames(RhinoDoc doc, IEnumerable<string> names)
    {
      int li = LayerIndex(doc, false); if (li < 0) return;
      var set = new HashSet<string>(names);
      foreach (var o in doc.Objects.GetObjectList(Filter(li)).ToArray()) if (set.Contains(o.Attributes.Name)) doc.Objects.Delete(o, true);
      doc.Views.Redraw();
    }

    static Color GroupColor(int i)
    {
      double h = (i * 0.61803398875) % 1.0, s = 0.85, v = 0.95;   // golden ratio hues, well separated
      double r = 0, g = 0, b = 0; int k = (int)(h * 6); double f = h * 6 - k, p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
      switch (k % 6) { case 0: r = v; g = t; b = p; break; case 1: r = q; g = v; b = p; break; case 2: r = p; g = v; b = t; break; case 3: r = p; g = q; b = v; break; case 4: r = t; g = p; b = v; break; default: r = v; g = p; b = q; break; }
      return Color.FromArgb((int)(r * 255), (int)(g * 255), (int)(b * 255));
    }
    static Color ColorOf(string name) => GroupColor((int.TryParse(name.TrimStart('P'), out int k) ? k : 1) - 1);

    // ---- picking, once outside the solution ----
    void Pick(object sender, EventArgs e)
    {
      RhinoApp.Idle -= Pick;
      var doc = RhinoDoc.ActiveDoc;
      var ed = Instances.DocumentEditor;
      var cams = _cams;
      var list = new List<Point3d>();
      double scale = 1;
      if (cams.Count > 1)
      {
        var cen = new Point3d(cams.Average(c => c.Cam.Origin.X), cams.Average(c => c.Cam.Origin.Y), cams.Average(c => c.Cam.Origin.Z));
        scale = Math.Max(1e-6, cams.Average(c => c.Cam.Origin.DistanceTo(cen)));
      }
      try
      {
        if (ed != null) ed.WindowState = System.Windows.Forms.FormWindowState.Minimized;   // minimise, not hide: hiding also hides the preview
        var existing = new HashSet<string>(Groups(doc).Select(g => g.name));
        int next = 1; while (existing.Contains("P" + next.ToString("000"))) next++;
        string name = "P" + next.ToString("000");
        var col = GroupColor(next - 1);
        while (true)
        {
          var lines = new List<(int cam, Line l)>(); var preds = new List<(int cam, Point3d q)>(); var seen = new HashSet<int>();
          var used = new HashSet<int>(list.Select(q => PhotoCore.CamAt(cams, q)));
          var ro = new List<Point3d>(); var rd = new List<Vector3d>(); var rc = new List<PhotoCam>();
          foreach (var q in list) { int j = PhotoCore.CamAt(cams, q); if (j >= 0) { ro.Add(cams[j].Cam.Origin); rd.Add(cams[j].Ray(q)); rc.Add(cams[j]); } }
          string res = "";
          if (rd.Count == 1)
            for (int j = 0; j < cams.Count; j++) { if (used.Contains(j)) continue; if (cams[j].Epipolar(ro[0], rd[0], scale, out var l)) { lines.Add((j, l)); seen.Add(j); } }
          if (rd.Count >= 2)
          {
            var X = PhotoCore.Intersect(ro, rd);
            if (X.IsValid)
            {
              for (int j = 0; j < cams.Count; j++) if (!used.Contains(j) && cams[j].Project(X, out var q)) { preds.Add((j, q)); seen.Add(j); }
              double px = 0;
              for (int i = 0; i < rd.Count; i++) { double t = (X - ro[i]) * rd[i]; px = Math.Max(px, X.DistanceTo(ro[i] + t * rd[i]) / Math.Max(1e-12, t) * rc[i].Fx); }
              res = string.Format(", residual {0:0.0} px", px);
            }
          }
          var grey = rd.Count == 0 ? new List<BoundingBox>() : Enumerable.Range(0, cams.Count).Where(j => !seen.Contains(j) && !used.Contains(j)).Select(j => cams[j].Box).ToList();

          var gp = new Rhino.Input.Custom.GetPoint();
          gp.SetCommandPrompt(string.Format("Megabat Measure {0}: click {1}{2} (Enter = done, Esc = cancel)", name, list.Count + 1, res));
          gp.Constrain(Plane.WorldXY, false);
          gp.AcceptNothing(true);
          var opt = new Rhino.Input.Custom.OptionToggle(_snap, "Off", "On");
          if (cams.Count > 0) gp.AddOptionToggle("Snap", ref opt);
          foreach (var q in list) gp.AddConstructionPoint(q);
          gp.DynamicDraw += (s, a) =>
          {
            foreach (var b in grey)
              a.Display.DrawPolygon(new[] { new Point3d(b.Min.X, b.Min.Y, 0), new Point3d(b.Max.X, b.Min.Y, 0), new Point3d(b.Max.X, b.Max.Y, 0), new Point3d(b.Min.X, b.Max.Y, 0) },
                Color.FromArgb(150, 60, 60, 60), true);
            foreach (var l in lines) a.Display.DrawLine(l.l, col, 2);
            foreach (var p in preds)
            {
              double rr = 0.02 * cams[p.cam].Box.Diagonal.X;
              a.Display.DrawLine(p.q - new Vector3d(rr, 0, 0), p.q + new Vector3d(rr, 0, 0), col, 2);
              a.Display.DrawLine(p.q - new Vector3d(0, rr, 0), p.q + new Vector3d(0, rr, 0), col, 2);
              a.Display.DrawCircle(new Circle(p.q, 0.4 * rr), col, 1);
            }
            foreach (var q in list) a.Display.DrawPoint(q, PointStyle.RoundSimple, 6, col);
          };
          var r = gp.Get();
          if (r == Rhino.Input.GetResult.Option) { _snap = opt.CurrentValue; continue; }
          if (r == Rhino.Input.GetResult.Point)
          {
            var q = gp.Point();
            if (_snap) { int j = PhotoCore.CamAt(cams, q); foreach (var l in lines) if (l.cam == j) q = l.l.ClosestPoint(q, true); }
            list.Add(q); continue;
          }
          if (r == Rhino.Input.GetResult.Nothing && list.Count >= 2)
          {
            var att = doc.CreateDefaultAttributes();
            att.LayerIndex = LayerIndex(doc, true); att.Name = name;
            att.ColorSource = ObjectColorSource.ColorFromObject; att.ObjectColor = col;
            uint sn = doc.BeginUndoRecord("Megabat Measure " + name);
            foreach (var q in list) doc.Objects.AddPoint(q, att);
            doc.EndUndoRecord(sn);
            doc.Views.Redraw();
          }
          else if (r == Rhino.Input.GetResult.Nothing) RhinoApp.WriteLine("Megabat Measure: at least 2 clicks needed, point discarded");
          break;
        }
      }
      finally
      {
        if (ed != null) { ed.WindowState = System.Windows.Forms.FormWindowState.Normal; ed.BringToFront(); }
        _busy = false;
        ExpireSolution(true);
      }
    }

    // ---- update when measure points are added, moved, renamed or deleted in Rhino ----
    void Hook(bool on)
    {
      if (on == _hooked) return;
      if (on)
      {
        RhinoDoc.AddRhinoObject += OnObj; RhinoDoc.DeleteRhinoObject += OnObj; RhinoDoc.UndeleteRhinoObject += OnObj;
        RhinoDoc.ReplaceRhinoObject += OnReplace; RhinoDoc.ModifyObjectAttributes += OnAttr;
      }
      else
      {
        RhinoDoc.AddRhinoObject -= OnObj; RhinoDoc.DeleteRhinoObject -= OnObj; RhinoDoc.UndeleteRhinoObject -= OnObj;
        RhinoDoc.ReplaceRhinoObject -= OnReplace; RhinoDoc.ModifyObjectAttributes -= OnAttr;
      }
      _hooked = on;
    }
    void OnObj(object s, RhinoObjectEventArgs e) => Touch(e.TheObject);
    void OnReplace(object s, RhinoReplaceObjectEventArgs e) => Touch(e.NewRhinoObject);
    void OnAttr(object s, RhinoModifyObjectAttributesEventArgs e) => Touch(e.RhinoObject);

    void Touch(RhinoObject o)
    {
      if (_busy || _pending || o == null || !(o.Geometry is Rhino.Geometry.Point)) return;
      var doc = o.Document; int li = doc == null ? -1 : LayerIndex(doc, false);
      if (li < 0 || o.Attributes.LayerIndex != li) return;
      _pending = true;
      EventHandler once = null;
      once = (s, e) => { RhinoApp.Idle -= once; _pending = false; ExpireSolution(true); };
      RhinoApp.Idle += once;
    }

    // ---- preview: name next to each click, in the point colour ----
    public override BoundingBox ClippingBox
    {
      get { var bb = BoundingBox.Empty; foreach (var g in _groups) foreach (var p in g.pts) bb.Union(p); return bb; }
    }
    public override void DrawViewportWires(IGH_PreviewArgs args)
    {
      if (Hidden || Locked) return;
      foreach (var g in _groups)
      {
        var col = ColorOf(g.name);
        foreach (var p in g.pts) args.Display.Draw2dText(g.name, col, p, false, 13, "Arial");
      }
    }
    public override void DrawViewportMeshes(IGH_PreviewArgs args) { }
  }

  public class IntersectComponent : PhotoComponent
  {
    public IntersectComponent() : base("Intersect", "Intersect",
      "Forward intersection: one 3D point per branch of image clicks (from Measure), least squares. Result in COLMAP units; use Georef for scale and position.", Tab.Photo) { }
    public override Guid ComponentGuid => new Guid("a3c9f0d2-11e4-4b6a-8a01-6d2f5e000053");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override string IconText => "IP";
    protected override Color IconColor => Icons.Photo;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
      p.AddPointParameter("P", "P", "Clicks on the image board, one branch per point (Measure › P)", GH_ParamAccess.tree);
      AddPhotos(p, false);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
      p.AddPointParameter("Pt", "Pt", "3D point per branch (invalid if fewer than 2 rays)", GH_ParamAccess.list);
      p.AddLineParameter("Rays", "Rays", "Lines camera → point, one branch per point", GH_ParamAccess.tree);
      p.AddNumberParameter("Res", "Res", "Distance of each ray to the point, one branch per point", GH_ParamAccess.tree);
      p.AddNumberParameter("Angle", "Angle", "Largest intersection angle between two rays [°]; small angles give poor depth", GH_ParamAccess.list);
      p.AddTextParameter("Info", "Info", "Number of points and rays, problems per branch", GH_ParamAccess.item);
    }
    protected override void SolveInstance(IGH_DataAccess DA)
    {
      if (!DA.GetDataTree(0, out GH_Structure<GH_Point> P)) return;
      var cams = Photo.ReadPhotos(DA, 1);
      if (cams.Count == 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "no photos"); return; }
      var pts = new List<Point3d>(); var rays = new DataTree<Line>(); var res = new DataTree<double>(); var ang = new List<double>(); var notes = new List<string>();
      for (int b = 0; b < P.PathCount; b++)
      {
        var path = P.Paths[b];
        var org = new List<Point3d>(); var dir = new List<Vector3d>();
        foreach (var g in P.Branches[b])
        {
          if (g == null) continue;
          int j = PhotoCore.CamAt(cams, g.Value);
          if (j < 0) { notes.Add(path + ": click outside all images"); continue; }
          org.Add(cams[j].Cam.Origin); dir.Add(cams[j].Ray(g.Value));
        }
        var p = PhotoCore.Intersect(org, dir);
        pts.Add(p);
        if (!p.IsValid) { ang.Add(0); notes.Add(path + ": fewer than 2 rays"); rays.EnsurePath(path); res.EnsurePath(path); continue; }
        for (int i = 0; i < dir.Count; i++)
        {
          rays.Add(new Line(org[i], p), path);
          double t = (p - org[i]) * dir[i];
          res.Add(p.DistanceTo(org[i] + t * dir[i]), path);
        }
        ang.Add(Math.Round(PhotoCore.MaxAngle(dir), 2));
      }
      DA.SetDataList(0, pts); DA.SetDataTree(1, rays); DA.SetDataTree(2, res); DA.SetDataList(3, ang);
      DA.SetData(4, string.Format("{0} points, {1} rays{2}; {3}", pts.Count(x => x.IsValid), rays.DataCount, notes.Count > 0 ? "; " + string.Join("; ", notes) : "", Ms));
    }
  }

  public class GeorefComponent : PhotoComponent
  {
    public GeorefComponent() : base("Georef", "Georef",
      "Transformation into a target coordinate system. With Dst (at least 3 points): Helmert transformation Src → Dst, least squares (rotation, translation, scale). Without Dst: Src[0] = origin, Src[1] on +X, Src[2] in the XY plane; Len = real distance Src[0]–Src[1] for the scale. Apply Xform with Transform to points, cameras or clouds.", Tab.Photo) { }
    public override Guid ComponentGuid => new Guid("a3c9f0d2-11e4-4b6a-8a01-6d2f5e000054");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override string IconText => "GR";
    protected override Color IconColor => Icons.Photo;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
      p.AddPointParameter("Src", "Src", "Points in the source system, e.g. Intersect › Pt", GH_ParamAccess.list);
      int a = p.AddPointParameter("Dst", "Dst", "Optional: the same points in the target system (control points), same order as Src", GH_ParamAccess.list);
      p.AddNumberParameter("Len", "Len", "Without Dst: real distance between Src[0] and Src[1]; 0 = no scaling", GH_ParamAccess.item, 0);
      p.AddBooleanParameter("scale", "scale", "With Dst: false = rigid (no scaling)", GH_ParamAccess.item, true);
      Optional(p, a);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
      p.AddTransformParameter("Xform", "Xform", "Transformation source → target", GH_ParamAccess.item);
      p.AddNumberParameter("Res", "Res", "With Dst: distance of each transformed Src point to its Dst point", GH_ParamAccess.list);
      p.AddNumberParameter("Scale", "Scale", "Scale factor", GH_ParamAccess.item);
      p.AddTextParameter("Info", "Info", "Mode, number of points, RMS", GH_ParamAccess.item);
    }
    protected override void SolveInstance(IGH_DataAccess DA)
    {
      var src = new List<Point3d>(); var dst = new List<Point3d>(); double len = 0; bool ws = true;
      if (!DA.GetDataList(0, src)) return;
      DA.GetDataList(1, dst); DA.GetData(2, ref len); DA.GetData(3, ref ws);
      Transform xf; double s;
      if (dst.Count > 0)
      {
        if (dst.Count != src.Count) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Src and Dst must have the same length"); return; }
        var a = new List<Point3d>(); var b = new List<Point3d>(); var idx = new List<int>();
        for (int i = 0; i < src.Count; i++) if (src[i].IsValid && dst[i].IsValid) { a.Add(src[i]); b.Add(dst[i]); idx.Add(i); }
        if (a.Count < 3) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "at least 3 valid point pairs needed"); return; }
        if (!PhotoCore.Helmert(a, b, ws, out xf, out s)) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "points are degenerate (collinear or identical)"); return; }
        var res = new List<double>(); double sq = 0;
        for (int i = 0; i < src.Count; i++)
        {
          if (!src[i].IsValid || !dst[i].IsValid) { res.Add(double.NaN); continue; }
          var q = src[i]; q.Transform(xf); double d = q.DistanceTo(dst[i]); res.Add(d); sq += d * d;
        }
        double rms = Math.Sqrt(sq / a.Count);
        if (a.Count == 3) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "3 points: no redundancy, residuals only show the shape mismatch; 4 or more give a check");
        DA.SetData(0, xf); DA.SetDataList(1, res); DA.SetData(2, s);
        DA.SetData(3, string.Format("Helmert{0}, {1} points, scale {2:0.######}, RMS {3:0.####}, max {4:0.####}; {5}", ws ? "" : " (rigid)", a.Count, s, rms, res.Where(x => !double.IsNaN(x)).Max(), Ms));
      }
      else
      {
        if (src.Count < 3 || !src[0].IsValid || !src[1].IsValid || !src[2].IsValid) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "without Dst: 3 valid points (origin, on X, in XY)"); return; }
        if (!PhotoCore.ThreePoint(src[0], src[1], src[2], len, out xf, out s)) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "the 3 points are collinear"); return; }
        DA.SetData(0, xf); DA.SetData(2, s);
        DA.SetData(3, string.Format("3 points: origin Src[0], X towards Src[1], XY through Src[2]; scale {0:0.######}{1}; {2}", s, len > 0 ? "" : " (no Len)", Ms));
      }
    }
  }

  public class DeconstructPhotoComponent : PhotoComponent
  {
    public DeconstructPhotoComponent() : base("Deconstruct Photo", "DePhoto",
      "Split a photo into camera plane, image rectangle, name, file and intrinsics.", Tab.Photo) { }
    public override Guid ComponentGuid => new Guid("a3c9f0d2-11e4-4b6a-8a01-6d2f5e000055");
    public override GH_Exposure Exposure => GH_Exposure.secondary;
    protected override string IconText => "DP";
    protected override Color IconColor => Icons.Photo;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
      p.AddParameter(new PhotoParam(), "Photo", "Photo", "Photo", GH_ParamAccess.item);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
      p.AddPlaneParameter("Cam", "Cam", "Camera plane: origin = projection centre, X/Y = image axes, Z = viewing direction", GH_ParamAccess.item);
      p.AddRectangleParameter("Frame", "Frame", "Rectangle of the image on the board", GH_ParamAccess.item);
      p.AddTextParameter("Name", "Name", "Image file name", GH_ParamAccess.item);
      p.AddTextParameter("Path", "Path", "Full path of the image file (empty if not found)", GH_ParamAccess.item);
      p.AddNumberParameter("fx", "fx", "Focal length in x [px]", GH_ParamAccess.item);
      p.AddNumberParameter("fy", "fy", "Focal length in y [px]", GH_ParamAccess.item);
      p.AddNumberParameter("cx", "cx", "Principal point x [px]", GH_ParamAccess.item);
      p.AddNumberParameter("cy", "cy", "Principal point y [px]", GH_ParamAccess.item);
      p.AddIntegerParameter("W", "W", "Image width [px]", GH_ParamAccess.item);
      p.AddIntegerParameter("H", "H", "Image height [px]", GH_ParamAccess.item);
    }
    protected override void SolveInstance(IGH_DataAccess DA)
    {
      PhotoGoo g = null;
      if (!DA.GetData(0, ref g) || g?.Value == null) return;
      var p = g.Value;
      DA.SetData(0, p.Cam); DA.SetData(1, p.Frame); DA.SetData(2, p.Name); DA.SetData(3, p.Path);
      DA.SetData(4, p.Fx); DA.SetData(5, p.Fy); DA.SetData(6, p.Cx); DA.SetData(7, p.Cy);
      DA.SetData(8, (int)p.W); DA.SetData(9, (int)p.H);
    }
  }

  public class OrthoComponent : PhotoComponent
  {
    public OrthoComponent() : base("Ortho", "Ortho",
      "Orthophoto of a planar face, e.g. a facade: a plane is fitted through the corner points, every pixel on it is projected into the photo that sees the face best (others fill gaps). Only the plane is rectified: projecting parts (cornices, balconies) are distorted. Occlusion is not checked.", Tab.Photo) { }
    public override Guid ComponentGuid => new Guid("a3c9f0d2-11e4-4b6a-8a01-6d2f5e000056");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override string IconText => "OR";
    protected override Color IconColor => Icons.Photo;

    bool _bake0;
    string _preview; Mesh _quad; DisplayMaterial _mat; BoundingBox _bb = BoundingBox.Empty;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
      p.AddPointParameter("Pts", "Pts", "Corner points of the face (at least 3), e.g. Intersect › Pt or after Georef", GH_ParamAccess.list);
      AddPhotos(p, false);
      int a = p.AddTransformParameter("Xform", "Xform", "Optional: Georef › Xform, if Pts are in the target system; the photos stay in the COLMAP system", GH_ParamAccess.item);
      int b = p.AddNumberParameter("px", "px", "Pixel size in the units of Pts; empty = longest side 2000 px", GH_ParamAccess.item);
      p.AddIntegerParameter("photo", "photo", "Index of the photo to use; -1 = automatic (best view, others fill gaps)", GH_ParamAccess.item, -1);
      p.AddBooleanParameter("sort", "sort", "true = order the corners by angle (convex outlines); false = use the given order", GH_ParamAccess.item, true);
      int c = p.AddTextParameter("file", "file", "PNG file; empty = temp folder", GH_ParamAccess.item);
      p.AddBooleanParameter("bake", "bake", "true = add the image as picture frame on Megabat::Ortho (embedded)", GH_ParamAccess.item, false);
      Optional(p, a, b, c);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
      p.AddTextParameter("Image", "Image", "Path of the PNG (transparent outside the face)", GH_ParamAccess.item);
      p.AddRectangleParameter("Frame", "Frame", "Rectangle of the image in the units of Pts", GH_ParamAccess.item);
      p.AddBrepParameter("Srf", "Srf", "Planar face from the corner points", GH_ParamAccess.item);
      p.AddTextParameter("Photo", "Photo", "Main photo", GH_ParamAccess.item);
      p.AddTextParameter("Info", "Info", "Size, pixel size, plane deviation, share seen, photos used", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      var pts = new List<Point3d>(); Transform xf = Transform.Identity; double px = 0; int force = -1; bool sort = true, bake = false; string file = null;
      if (!DA.GetDataList(0, pts)) return;
      var cams = Photo.ReadPhotos(DA, 1);
      DA.GetData(2, ref xf); DA.GetData(3, ref px); DA.GetData(4, ref force); DA.GetData(5, ref sort); DA.GetData(6, ref file); DA.GetData(7, ref bake);
      pts = pts.Where(q => q.IsValid).ToList();
      if (pts.Count < 3) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "at least 3 valid points"); return; }
      if (cams.Count == 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "no photos"); return; }
      if (!xf.TryGetInverse(out Transform toPhoto)) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Xform cannot be inverted"); return; }

      var r = OrthoCore.Run(cams, pts, toPhoto, px, force, 8000, sort, i => Load(cams[i].Path));
      if (r == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "points are degenerate"); return; }
      if (r.Seen == 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "no photo sees the face (check Xform and image files)"); return; }

      if (string.IsNullOrWhiteSpace(file)) file = Path.Combine(Path.GetTempPath(), "Megabat", "ortho_" + InstanceGuid.ToString("N").Substring(0, 8) + ".png");
      file = file.Trim().Trim('"');
      Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));
      Save(r, file);

      // preview from a fresh copy: Rhino caches textures by file name
      try { if (_preview != null && File.Exists(_preview)) File.Delete(_preview); } catch { }
      _preview = Path.Combine(Path.GetTempPath(), "Megabat", "ortho_preview_" + DateTime.Now.Ticks + ".png");
      File.Copy(file, _preview, true);
      var rect = new Rectangle3d(r.Plane, r.W * r.Px, r.H * r.Px);
      _quad = new Mesh();
      for (int k = 0; k < 4; k++) _quad.Vertices.Add(rect.Corner(k));
      _quad.TextureCoordinates.Add(0, 0); _quad.TextureCoordinates.Add(1, 0); _quad.TextureCoordinates.Add(1, 1); _quad.TextureCoordinates.Add(0, 1);
      _quad.Faces.AddFace(0, 1, 2, 3); _quad.Normals.ComputeNormals();
      _mat = new DisplayMaterial(Color.White); _mat.SetBitmapTexture(_preview, true); _mat.SetTransparencyTexture(_preview, true);
      _bb = rect.BoundingBox;

      double tol = RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.001;
      var srf = Brep.CreatePlanarBreps(new PolylineCurve(r.Outline), tol);
      string main = r.Best >= 0 ? cams[r.Best].Name : "";
      var used = Enumerable.Range(0, cams.Count).Where(i => r.Used[i] > 0).OrderByDescending(i => r.Used[i])
        .Select(i => string.Format("{0} {1:0} %", cams[i].Name, 100.0 * r.Used[i] / r.Seen)).ToList();
      string note = bake && !_bake0 ? "; " + Bake(file, rect) : "";
      _bake0 = bake;
      DA.SetData(0, file); DA.SetData(1, rect); DA.SetData(2, srf != null && srf.Length > 0 ? srf[0] : null); DA.SetData(3, main);
      DA.SetData(4, string.Format("{0} × {1} px, px = {2:0.####}, plane RMS {3:0.####}, seen {4:0} % of the face; photos: {5}{6}; {7}",
        r.W, r.H, r.Px, r.Rms, 100.0 * r.Seen / Math.Max(1, r.Inside), string.Join(", ", used.Take(5)) + (used.Count > 5 ? " ..." : ""), note, Ms));
    }

    static OrthoCore.Image Load(string path)
    {
      if (path == null || !File.Exists(path)) return null;
      using (var bmp = new Bitmap(path))
      {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        var m = new OrthoCore.Image { W = bmp.Width, H = bmp.Height, Bgr = new byte[bmp.Width * bmp.Height * 3] };
        for (int y = 0; y < m.H; y++)
          System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, m.Bgr, y * m.W * 3, m.W * 3);
        bmp.UnlockBits(data);
        return m;
      }
    }

    static void Save(OrthoCore.Result r, string file)
    {
      using (var bmp = new Bitmap(r.W, r.H, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
      {
        var data = bmp.LockBits(new Rectangle(0, 0, r.W, r.H), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (int y = 0; y < r.H; y++)
          System.Runtime.InteropServices.Marshal.Copy(r.Bgra, y * r.W * 4, data.Scan0 + y * data.Stride, r.W * 4);
        bmp.UnlockBits(data);
        bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
      }
    }

    string Bake(string file, Rectangle3d rect)
    {
      var doc = RhinoDoc.ActiveDoc; if (doc == null) return "no Rhino document";
      int li = Photo.Layer(doc, "Ortho", Photo.Layer(doc, "Megabat", -1));
      var id = doc.Objects.AddPictureFrame(rect.Plane, file, true, rect.Width, rect.Height, true, true);
      var obj = doc.Objects.FindId(id);
      if (obj == null) return "bake failed";
      obj.Attributes.LayerIndex = li; obj.Attributes.Name = Path.GetFileNameWithoutExtension(file); obj.CommitChanges();
      doc.Views.Redraw();
      return "baked to Megabat::Ortho";
    }

    public override BoundingBox ClippingBox => _bb;
    public override void DrawViewportMeshes(IGH_PreviewArgs args)
    {
      if (Hidden || Locked || _quad == null) return;
      args.Display.DrawMeshShaded(_quad, _mat);
    }
  }
}
