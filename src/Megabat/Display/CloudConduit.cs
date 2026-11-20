using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Display;
using Rhino.Geometry;

namespace Megabat.Display
{
  // Clouds that draw themselves through this conduit instead of the Grasshopper preview
  public interface IConduitCloud
  {
    BoundingBox PreviewBox { get; }
    // mode: 0 = global setting, 1 = thinned, 2 = level of detail (from the Cloud parameter)
    void DrawCloud(DisplayPipeline pipeline, Color color, int mode);
  }

  // Draws the Megabat clouds of the active Grasshopper document in an own Rhino display conduit.
  // Point clouds drawn by the Grasshopper preview take on the selection colour (yellow) whenever
  // something is picked in Rhino; drawn here they keep their colours. Visibility follows the
  // Grasshopper rules: preview on, object not hidden, "selected only" filter, output not hidden.
  public class CloudConduit : DisplayConduit
  {
    static CloudConduit _c;
    static MethodInfo _canDraw, _canBox;
    public static bool Active => _c != null && _c.Enabled;

    public static void Start()
    {
      if (_c != null) return;
      var bf = BindingFlags.Instance | BindingFlags.NonPublic;
      _canDraw = typeof(GH_Document).GetMethod("CanDrawPreview", bf, null, new[] { typeof(DrawEventArgs) }, null);
      _canBox = typeof(GH_Document).GetMethod("CanDrawPreview", bf, null, Type.EmptyTypes, null);
      _c = new CloudConduit { Enabled = true };
    }

    static bool Can(GH_Document d, MethodInfo m, object[] a)
    {
      if (d == null || d.PreviewMode == GH_PreviewMode.Disabled) return false;
      if (m == null) return true;
      try { return (bool)m.Invoke(d, a); } catch { return true; }
    }

    static IEnumerable<(IConduitCloud cloud, bool sel, int mode)> Items(GH_Document d)
    {
      bool selOnly = d.PreviewFilter == GH_PreviewFilter.Selected;
      foreach (var o in d.Objects)
      {
        if (!(o is IGH_PreviewObject po) || po.Hidden) continue;
        bool sel = o.Attributes.Selected;
        if (selOnly && !sel) continue;
        IEnumerable<IGH_Param> ps;
        if (o is IGH_Component c) ps = c.Params.Output.Where(p => !(p is IGH_PreviewObject pp && pp.Hidden));
        else if (o is IGH_Param p1) ps = new[] { p1 };
        else continue;
        foreach (var p in ps)
          foreach (var g in p.VolatileData.AllData(true))
            if (g is IConduitCloud cc) yield return (cc, sel, p is Types.CloudParam cp ? cp.DisplayMode : 0);
      }
    }

    protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
    {
      var d = Instances.ActiveCanvas?.Document;
      if (!Can(d, _canBox, null)) return;
      try { foreach (var (cc, _, _) in Items(d)) e.IncludeBoundingBox(cc.PreviewBox); } catch { }
    }

    protected override void PostDrawObjects(DrawEventArgs e)
    {
      var d = Instances.ActiveCanvas?.Document;
      if (!Can(d, _canDraw, new object[] { e })) return;
      try { foreach (var (cc, sel, mode) in Items(d)) cc.DrawCloud(e.Display, sel ? d.PreviewColourSelected : d.PreviewColour, mode); } catch { }
    }
  }
}
