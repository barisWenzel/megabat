// Megabat test: measure image points for intersection as Rhino points.
// Add: the Grasshopper window hides, you click the same feature in 2 or 3 images (XY plane), Enter.
// The clicks become Rhino points on the layer Megabat::Measure, named P001, P002 ..., one colour per name.
// Move, copy or delete them in Rhino as you like; points with the same name belong to the same 3D point.
// Inputs:  Add, Undo (delete the last name), Clear (delete all measure points)  (buttons);
//          Cam, K, Frame (optional, from ImportColmap): epipolar lines after the first click, predicted position after the second
//          click, snapping onto them (option Snap), images that cannot see the point are greyed out
// Outputs: P (tree, one branch per name), Name (one per branch), Info
#region Usings
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
#endregion

public class Script_Instance : GH_ScriptInstance
{
  const string Parent = "Megabat", Child = "Measure";
  bool _add0, _undo0, _clear0, _busy;
  List<(string name, List<Point3d> pts)> _groups = new List<(string, List<Point3d>)>();

  class Cam { public Point3d C; public Vector3d X, Y, Z; public double Fx, Fy, Cx, Cy, W, H; public BoundingBox Box; public double S; }
  List<Cam> _cams = new List<Cam>();
  bool _snap = true;

  private void RunScript(object Add, object Undo, object Clear, List<object> Cam, List<object> K, List<object> Frame,
    ref object P, ref object Name, ref object Info)
  {
    Component.Message = "Measure";
    var doc = RhinoDoc.ActiveDoc;
    if (doc == null) return;
    SetCams(Cam, K, Frame);
    Hook();
    bool a = B(Add), u = B(Undo), c = B(Clear);
    if (a && !_add0 && !_busy) { _busy = true; RhinoApp.Idle += Pick; }
    if (u && !_undo0) DeleteNames(doc, Groups(doc).Select(g => g.name).OrderBy(x => x).Skip(Math.Max(0, Groups(doc).Count - 1)));
    if (c && !_clear0) DeleteNames(doc, Groups(doc).Select(g => g.name));
    _add0 = a; _undo0 = u; _clear0 = c;

    _groups = Groups(doc);
    var tree = new DataTree<Point3d>();
    for (int i = 0; i < _groups.Count; i++) tree.AddRange(_groups[i].pts, new GH_Path(i));
    P = tree;
    Name = _groups.Select(g => g.name).ToList();
    Info = string.Format("{0} points, {1} clicks, {2} with only one click{3}", _groups.Count, _groups.Sum(g => g.pts.Count),
      _groups.Count(g => g.pts.Count < 2), _busy ? " (picking ...)" : "") + (_cams.Count > 0 ? ", " + _cams.Count + " cameras for epipolar lines" : "");
  }

  static bool B(object o) { return o != null && Convert.ToBoolean(o); }

  // ---- layer and points ----
  static int LayerIndex(RhinoDoc doc, bool create)
  {
    int p = -1;
    foreach (var l in doc.Layers) if (!l.IsDeleted && l.Name == Parent && l.ParentLayerId == Guid.Empty) p = l.Index;
    if (p < 0) { if (!create) return -1; p = doc.Layers.Add(new Layer { Name = Parent }); }
    var pid = doc.Layers[p].Id;
    foreach (var l in doc.Layers) if (!l.IsDeleted && l.Name == Child && l.ParentLayerId == pid) return l.Index;
    if (!create) return -1;
    return doc.Layers.Add(new Layer { Name = Child, ParentLayerId = pid });
  }

  static List<(string name, List<Point3d> pts)> Groups(RhinoDoc doc)
  {
    int li = LayerIndex(doc, false);
    var res = new List<(string, List<Point3d>)>();
    if (li < 0) return res;
    var objs = doc.Objects.GetObjectList(new ObjectEnumeratorSettings { LayerIndexFilter = li, DeletedObjects = false, HiddenObjects = true, LockedObjects = true }).ToArray();
    foreach (var g in objs.Where(o => o.Geometry is Rhino.Geometry.Point && !string.IsNullOrEmpty(o.Attributes.Name))
                          .GroupBy(o => o.Attributes.Name).OrderBy(g => g.Key))
      res.Add((g.Key, g.Select(o => ((Rhino.Geometry.Point)o.Geometry).Location).ToList()));
    return res;
  }

  static void DeleteNames(RhinoDoc doc, IEnumerable<string> names)
  {
    int li = LayerIndex(doc, false); if (li < 0) return;
    var set = new HashSet<string>(names);
    var objs = doc.Objects.GetObjectList(new ObjectEnumeratorSettings { LayerIndexFilter = li, DeletedObjects = false, HiddenObjects = true, LockedObjects = true }).ToArray();
    foreach (var o in objs) if (set.Contains(o.Attributes.Name)) doc.Objects.Delete(o, true);
    doc.Views.Redraw();
  }

  static Color GroupColor(int i)
  {
    double h = (i * 0.61803398875) % 1.0;   // golden ratio hues, well separated
    return HsvToRgb(h, 0.85, 0.95);
  }
  static Color HsvToRgb(double h, double s, double v)
  {
    double r = 0, g = 0, b = 0; int k = (int)(h * 6); double f = h * 6 - k, p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
    switch (k % 6) { case 0: r = v; g = t; b = p; break; case 1: r = q; g = v; b = p; break; case 2: r = p; g = v; b = t; break; case 3: r = p; g = q; b = v; break; case 4: r = t; g = p; b = v; break; default: r = v; g = p; b = q; break; }
    return Color.FromArgb((int)(r * 255), (int)(g * 255), (int)(b * 255));
  }

  // ---- cameras and epipolar geometry ----
  static object Unwrap(object o) { return o is Grasshopper.Kernel.Types.IGH_Goo g ? g.ScriptVariable() : o; }

  void SetCams(List<object> cam, List<object> k, List<object> frame)
  {
    _cams = new List<Cam>();
    if (cam == null || k == null || frame == null || cam.Count != k.Count || cam.Count != frame.Count) return;
    var ci = System.Globalization.CultureInfo.InvariantCulture;
    for (int i = 0; i < cam.Count; i++)
    {
      if (!(Unwrap(cam[i]) is Plane pl) || !(Unwrap(frame[i]) is Rectangle3d fr)) continue;
      var kk = Unwrap(k[i]).ToString().Split(' ').Select(t => double.Parse(t, ci)).ToArray();
      _cams.Add(new Cam { C = pl.Origin, X = pl.XAxis, Y = pl.YAxis, Z = pl.ZAxis, Fx = kk[0], Fy = kk[1], Cx = kk[2], Cy = kk[3], W = kk[4], H = kk[5],
        Box = fr.BoundingBox, S = fr.Width / kk[4] });
    }
  }

  int CamAt(Point3d q) { return _cams.FindIndex(c => q.X >= c.Box.Min.X && q.X <= c.Box.Max.X && q.Y >= c.Box.Min.Y && q.Y <= c.Box.Max.Y); }

  // board click -> ray in model space
  Vector3d Ray(Cam c, Point3d q)
  {
    double u = (q.X - c.Box.Min.X) / c.S, v = (c.Box.Max.Y - q.Y) / c.S;
    var d = (u - c.Cx) / c.Fx * c.X + (v - c.Cy) / c.Fy * c.Y + c.Z; d.Unitize(); return d;
  }

  // model point -> board point in image c; false if behind the camera or outside the image
  bool Project(Cam c, Point3d p, out Point3d q)
  {
    var d = p - c.C; double z = d * c.Z; q = Point3d.Unset;
    if (z <= 1e-9) return false;
    double u = c.Fx * (d * c.X) / z + c.Cx, v = c.Fy * (d * c.Y) / z + c.Cy;
    if (u < 0 || v < 0 || u > c.W || v > c.H) return false;
    q = new Point3d(c.Box.Min.X + u * c.S, c.Box.Max.Y - v * c.S, 0); return true;
  }

  // epipolar line of a ray in image c: projection of ray points from close to far
  bool Epipolar(Cam c, Point3d o, Vector3d d, double scale, out Line l)
  {
    l = Line.Unset; Point3d first = Point3d.Unset, last = Point3d.Unset;
    for (int i = 0; i <= 400; i++)
    {
      double t = scale * 0.01 * Math.Pow(1e5, i / 400.0);
      if (Project(c, o + t * d, out var q)) { if (!first.IsValid) first = q; last = q; }
    }
    if (!first.IsValid || first.DistanceTo(last) < 1e-9) return false;
    l = new Line(first, last); return true;
  }

  static Point3d LeastSquares(List<Point3d> o, List<Vector3d> d)
  {
    var A = new Rhino.Geometry.Matrix(3, 3); double[] r = new double[3];
    for (int i = 0; i < d.Count; i++)
    {
      double[] dd = { d[i].X, d[i].Y, d[i].Z }, cc = { o[i].X, o[i].Y, o[i].Z };
      for (int m = 0; m < 3; m++) for (int n = 0; n < 3; n++) { double a = (m == n ? 1 : 0) - dd[m] * dd[n]; A[m, n] += a; r[m] += a * cc[n]; }
    }
    if (!A.Invert(1e-12)) return Point3d.Unset;
    return new Point3d(A[0, 0] * r[0] + A[0, 1] * r[1] + A[0, 2] * r[2], A[1, 0] * r[0] + A[1, 1] * r[1] + A[1, 2] * r[2], A[2, 0] * r[0] + A[2, 1] * r[1] + A[2, 2] * r[2]);
  }

  // Runs once outside the solution: hide Grasshopper, pick points until Enter, add them as Rhino points
  void Pick(object sender, EventArgs e)
  {
    RhinoApp.Idle -= Pick;
    var doc = RhinoDoc.ActiveDoc;
    var ed = Grasshopper.Instances.DocumentEditor;
    var list = new List<Point3d>();
    double scale = 1;
    if (_cams.Count > 1) { var cen = new Point3d(_cams.Average(c => c.C.X), _cams.Average(c => c.C.Y), _cams.Average(c => c.C.Z)); scale = Math.Max(1e-6, _cams.Average(c => c.C.DistanceTo(cen))); }
    try
    {
      if (ed != null) ed.WindowState = System.Windows.Forms.FormWindowState.Minimized;   // minimise, not hide: hiding also hides the preview
      var existing = Groups(doc).Select(g => g.name).ToList();
      int next = 1; while (existing.Contains("P" + next.ToString("000"))) next++;
      string name = "P" + next.ToString("000");
      var col = GroupColor(next - 1);
      while (true)
      {
        // guides from the clicks so far
        var lines = new List<(int cam, Line l)>(); var preds = new List<(int cam, Point3d q)>(); var seen = new HashSet<int>();
        var used = new HashSet<int>(list.Select(CamAt));
        var ro = new List<Point3d>(); var rd = new List<Vector3d>();
        foreach (var q in list) { int j = CamAt(q); if (j >= 0) { ro.Add(_cams[j].C); rd.Add(Ray(_cams[j], q)); } }
        string res = "";
        if (rd.Count == 1)
          for (int j = 0; j < _cams.Count; j++) { if (used.Contains(j)) continue; if (Epipolar(_cams[j], ro[0], rd[0], scale, out var l)) { lines.Add((j, l)); seen.Add(j); } }
        if (rd.Count >= 2)
        {
          var X = LeastSquares(ro, rd);
          if (X.IsValid)
          {
            for (int j = 0; j < _cams.Count; j++) if (!used.Contains(j) && Project(_cams[j], X, out var q)) { preds.Add((j, q)); seen.Add(j); }
            double px = 0; for (int i = 0; i < rd.Count; i++) { double t = (X - ro[i]) * rd[i]; var cj = _cams[CamAt(list[i])]; px = Math.Max(px, X.DistanceTo(ro[i] + t * rd[i]) / Math.Max(1e-12, t) * cj.Fx); }
            res = string.Format(", residual {0:0.0} px", px);
          }
        }
        var grey = rd.Count == 0 ? new List<BoundingBox>() : Enumerable.Range(0, _cams.Count).Where(j => !seen.Contains(j) && !used.Contains(j)).Select(j => _cams[j].Box).ToList();

        var gp = new Rhino.Input.Custom.GetPoint();
        gp.SetCommandPrompt(string.Format("Megabat Measure {0}: click {1}{2} (Enter = done, Esc = cancel)", name, list.Count + 1, res));
        gp.Constrain(Plane.WorldXY, false);
        gp.AcceptNothing(true);
        var opt = new Rhino.Input.Custom.OptionToggle(_snap, "Off", "On");
        if (_cams.Count > 0) gp.AddOptionToggle("Snap", ref opt);
        foreach (var q in list) gp.AddConstructionPoint(q);
        gp.DynamicDraw += (s, a) =>
        {
          foreach (var b in grey)
            a.Display.DrawPolygon(new[] { new Point3d(b.Min.X, b.Min.Y, 0), new Point3d(b.Max.X, b.Min.Y, 0), new Point3d(b.Max.X, b.Max.Y, 0), new Point3d(b.Min.X, b.Max.Y, 0) },
              Color.FromArgb(150, 60, 60, 60), true);
          foreach (var l in lines) a.Display.DrawLine(l.l, col, 2);
          foreach (var p in preds)
          {
            double rr = 0.02 * _cams[p.cam].Box.Diagonal.X;
            a.Display.DrawLine(p.q - new Vector3d(rr, 0, 0), p.q + new Vector3d(rr, 0, 0), col, 2);
            a.Display.DrawLine(p.q - new Vector3d(0, rr, 0), p.q + new Vector3d(0, rr, 0), col, 2);
            a.Display.DrawCircle(new Circle(p.q, 0.4 * rr), col, 1);
          }
          foreach (var q in list) a.Display.DrawPoint(q, Rhino.Display.PointStyle.RoundSimple, 6, col);
        };
        var r = gp.Get();
        if (r == Rhino.Input.GetResult.Option) { _snap = opt.CurrentValue; continue; }
        if (r == Rhino.Input.GetResult.Point)
        {
          var q = gp.Point();
          if (_snap)
          {
            int j = CamAt(q);
            foreach (var l in lines) if (l.cam == j) q = l.l.ClosestPoint(q, true);
          }
          list.Add(q); continue;
        }
        if (r == Rhino.Input.GetResult.Nothing && list.Count >= 2)
        {
          int li = LayerIndex(doc, true);
          var att = doc.CreateDefaultAttributes();
          att.LayerIndex = li; att.Name = name;
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
      Component.ExpireSolution(true);
    }
  }

  // ---- update when measure points are added, moved, renamed or deleted in Rhino ----
  void Hook()
  {
    string key = "Megabat.Measure.Hook." + Component.InstanceGuid;
    if (AppDomain.CurrentDomain.GetData(key) is Action old) old();   // remove handlers of a previous compile
    EventHandler<RhinoObjectEventArgs> h = (s, e) => Touch(e.TheObject);
    EventHandler<RhinoReplaceObjectEventArgs> hr = (s, e) => Touch(e.NewRhinoObject);
    EventHandler<RhinoModifyObjectAttributesEventArgs> ha = (s, e) => Touch(e.RhinoObject);
    RhinoDoc.AddRhinoObject += h; RhinoDoc.DeleteRhinoObject += h; RhinoDoc.UndeleteRhinoObject += h;
    RhinoDoc.ReplaceRhinoObject += hr; RhinoDoc.ModifyObjectAttributes += ha;
    AppDomain.CurrentDomain.SetData(key, (Action)(() =>
    {
      RhinoDoc.AddRhinoObject -= h; RhinoDoc.DeleteRhinoObject -= h; RhinoDoc.UndeleteRhinoObject -= h;
      RhinoDoc.ReplaceRhinoObject -= hr; RhinoDoc.ModifyObjectAttributes -= ha;
    }));
  }

  bool _pending;
  void Touch(RhinoObject o)
  {
    if (_busy || _pending || o == null || !(o.Geometry is Rhino.Geometry.Point)) return;
    var doc = o.Document; int li = doc == null ? -1 : LayerIndex(doc, false);
    if (li < 0 || o.Attributes.LayerIndex != li) return;
    _pending = true;
    EventHandler once = null;
    once = (s, e) => { RhinoApp.Idle -= once; _pending = false; Component.ExpireSolution(true); };
    RhinoApp.Idle += once;
  }

  // ---- preview: name next to each click, in the group colour ----
  public override BoundingBox ClippingBox
  {
    get { var bb = BoundingBox.Empty; foreach (var g in _groups) foreach (var p in g.pts) bb.Union(p); return bb; }
  }

  public override void DrawViewportWires(IGH_PreviewArgs args)
  {
    for (int i = 0; i < _groups.Count; i++)
    {
      int n = 1; if (int.TryParse(_groups[i].name.TrimStart('P'), out int k)) n = k;
      var col = GroupColor(n - 1);
      foreach (var p in _groups[i].pts) args.Display.Draw2dText(_groups[i].name, col, p, false, 13, "Arial");
    }
  }
}
