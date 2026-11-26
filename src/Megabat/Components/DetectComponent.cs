using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Megabat.Core;
using Megabat.Types;

namespace Megabat.Components
{
  public class DetectComponent : MegabatComponent
  {
    public DetectComponent() : base("Detect", "Detect",
      "Experimental: find objects named in a text prompt (e.g. \"radiator. table.\") with Grounding DINO. The cloud is rendered into views, the 2D boxes are mapped back to the points. Needs Python with Grounding DINO in a separate folder (see docs).", Tab.Elements) { }
    public override Guid ComponentGuid => new Guid("a3c9f0d2-11e4-4b6a-8a01-6d2f5e000026");
    public override GH_Exposure Exposure => GH_Exposure.secondary;
    protected override string IconText => "AI";
    protected override Color IconColor => Icons.Elements;

    List<CloudGoo> _clouds = new List<CloudGoo>(); List<string> _labels = new List<string>();
    List<double> _scores = new List<double>(); List<Box> _boxes = new List<Box>(); string _info = "set run = true";

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
      AddCloud(p, "C", "Point cloud, with colours if possible; clouds thinned to more than about 2 cm give poor views");
      p.AddTextParameter("prompt", "prompt", "Classes in English, separated by a dot and a space, e.g. \"radiator. door. table.\"", GH_ParamAccess.item);
      p.AddGenericParameter("cam", "cam", "Optional: standpoints (points, e.g. from ViewPoints; 'views' horizontal views each) and/or names of Rhino named views, 'active' = current view; empty = centre of the cloud", GH_ParamAccess.list);
      p.AddIntegerParameter("views", "views", "Horizontal views per standpoint", GH_ParamAccess.item, 8);
      p.AddNumberParameter("score", "score", "Minimum detection score (0 to 1)", GH_ParamAccess.item, 0.45);
      p.AddNumberParameter("depth", "depth", "Max. depth of an object behind its front [m]", GH_ParamAccess.item, 0.5);
      p.AddIntegerParameter("minViews", "minViews", "An object must be detected in at least this many views; false detections rarely repeat", GH_ParamAccess.item, 2);
      p.AddBooleanParameter("run", "run", "true = run (about 30 to 60 s, the model is loaded each time); results are kept when set back to false", GH_ParamAccess.item, false);
      p.AddTextParameter("folder", "folder", "Folder with venv\\Scripts\\python.exe and detect.py", GH_ParamAccess.item, @"C:\megabat_detect");
      Optional(p, 2);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
      AddCloud(p, "Clouds", "Points of each object", GH_ParamAccess.list);
      p.AddTextParameter("Label", "Label", "Class of each object", GH_ParamAccess.list);
      p.AddNumberParameter("Score", "Score", "Best detection score of each object", GH_ParamAccess.list);
      p.AddBoxParameter("Box", "Box", "3D bounding box of each object", GH_ParamAccess.list);
      p.AddTextParameter("Info", "Info", "Objects per class, detections per view, dropped objects, run time", GH_ParamAccess.item);
    }
    protected override void SolveInstance(IGH_DataAccess DA)
    {
      CloudGoo cg = null; string prompt = null; var cam = new List<IGH_Goo>();
      int views = 8, minViews = 2; double score = 0.45, depth = 0.5; bool run = false; string folder = @"C:\megabat_detect";
      DA.GetData(0, ref cg); DA.GetData(1, ref prompt); DA.GetDataList(2, cam);
      DA.GetData(3, ref views); DA.GetData(4, ref score); DA.GetData(5, ref depth); DA.GetData(6, ref minViews); DA.GetData(7, ref run); DA.GetData(8, ref folder);

      if (run && cg?.Value != null && cg.Value.Count > 0 && !string.IsNullOrWhiteSpace(prompt))
      {
        var sp = new List<Point3d>(); var vps = new List<Rhino.DocObjects.ViewportInfo>();
        var doc = Rhino.RhinoDoc.ActiveDoc;
        foreach (var g in cam)
        {
          if (g == null) continue;
          if (!(g is GH_String) && g.CastTo(out Point3d p)) { sp.Add(p); continue; }
          if (g is GH_String gs && doc != null)
          {
            var s = gs.Value.Trim();
            if (s.Equals("active", StringComparison.OrdinalIgnoreCase)) { vps.Add(new Rhino.DocObjects.ViewportInfo(doc.Views.ActiveView.ActiveViewport)); continue; }
            int idx = doc.NamedViews.FindByName(s);
            if (idx >= 0) vps.Add(doc.NamedViews[idx].Viewport);
            else AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Named view not found: " + s);
            continue;
          }
          AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "cam: ignored " + g.TypeName);
        }
        var r = DetectCore.Run(cg.Value, prompt, sp, vps, new DetectCore.Options { Folder = Resolve(folder), Views = views, Score = score, Depth = depth, MinViews = minViews });
        if (r.Error != null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, r.Error); _info = r.Error; }
        else
        {
          _clouds = r.Objects.Select(x => new CloudGoo(x.Cloud)).ToList();
          _labels = r.Objects.Select(x => x.Label).ToList();
          _scores = r.Objects.Select(x => x.Score).ToList();
          _boxes = r.Objects.Select(x => x.Box).ToList();
          _info = r.Info + ", " + Ms;
        }
      }
      DA.SetDataList(0, _clouds); DA.SetDataList(1, _labels); DA.SetDataList(2, _scores); DA.SetDataList(3, _boxes);
      DA.SetData(4, _info);
    }
  }
}
