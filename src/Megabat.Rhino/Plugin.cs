using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;
using Megabat.Core;

[assembly: System.Runtime.InteropServices.Guid("8d2c4b1e-5f7a-4c39-9e61-3b0a7d4f2c90")]
[assembly: Rhino.PlugIns.PlugInDescription(Rhino.PlugIns.DescriptionType.Organization, "IfAG, University of Stuttgart")]
[assembly: Rhino.PlugIns.PlugInDescription(Rhino.PlugIns.DescriptionType.WebSite, "https://github.com/barisWenzel/megabat")]

namespace Megabat.RhinoPlugin
{
  // Megabat for Rhino: large point clouds as light proxies.
  // The document holds a thinned cloud; the full cloud lives in a file next to the .3dm.
  // Megabat in Grasshopper recognises the proxy and computes with the full file.
  public class MegabatRhino : Rhino.PlugIns.PlugIn
  {
    public static MegabatRhino Instance { get; private set; }
    public MegabatRhino() { Instance = this; }
    public override Rhino.PlugIns.PlugInLoadTime LoadTime => Rhino.PlugIns.PlugInLoadTime.AtStartup;

    internal const int DefaultProxy = 500000;
    internal int ProxyCount
    {
      get => Settings.GetInteger("ProxyCount", DefaultProxy);
      set => Settings.SetInteger("ProxyCount", value);
    }

    internal static Guid AddProxy(RhinoDoc doc, PointCloud full, string file, string name, int count, ObjectAttributes att = null)
    {
      var proxy = CloudUtil.Thin(full, count);
      if (ReferenceEquals(proxy, full)) proxy = (PointCloud)full.Duplicate();
      att = att?.Duplicate() ?? doc.CreateDefaultAttributes();
      if (string.IsNullOrEmpty(att.Name)) att.Name = name;
      CloudFile.MarkProxy(att, file, doc.Path, full.Count);
      return doc.Objects.AddPointCloud(proxy, att);
    }

    internal static bool AskCount(string prompt, ref int count)
    {
      var gi = new GetInteger();
      gi.SetCommandPrompt(prompt);
      gi.SetDefaultInteger(count);
      gi.SetLowerLimit(1000, false);
      gi.AcceptNothing(true);
      var r = gi.Get();
      if (r == GetResult.Nothing) return true;
      if (r != GetResult.Number) return false;
      count = gi.Number();
      return true;
    }
  }

  // Import a point cloud file as proxy; the file stays where it is
  public class MegabatImportCommand : Command
  {
    public override string EnglishName => "MegabatImport";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
      var fd = new Rhino.UI.OpenFileDialog
      {
        Title = "Megabat: import point cloud as proxy",
        Filter = "Point clouds (*.e57;*.ply;*.3dm;*.xyz;*.pts;*.txt;*.csv)|*.e57;*.ply;*.3dm;*.xyz;*.pts;*.txt;*.csv",
        MultiSelect = true
      };
      if (!fd.ShowOpenDialog()) return Result.Cancel;
      int count = MegabatRhino.Instance.ProxyCount;
      if (!MegabatRhino.AskCount("Points shown in Rhino", ref count)) return Result.Cancel;
      MegabatRhino.Instance.ProxyCount = count;

      int done = 0;
      foreach (var file in fd.FileNames)
      {
        try
        {
          RhinoApp.SetCommandPrompt("Reading " + Path.GetFileName(file) + " ...");
          var sw = System.Diagnostics.Stopwatch.StartNew();
          var full = CloudFile.Load(file, doc.ModelUnitSystem);
          MegabatRhino.AddProxy(doc, full, file, Path.GetFileNameWithoutExtension(file), count);
          RhinoApp.WriteLine(string.Format("Megabat: {0} with {1:N0} points, {2:N0} shown ({3:0.0} s)",
            Path.GetFileName(file), full.Count, Math.Min(count, full.Count), sw.Elapsed.TotalSeconds));
          done++;
        }
        catch (Exception e) { RhinoApp.WriteLine("Megabat: " + Path.GetFileName(file) + ": " + e.Message); }
      }
      doc.Views.Redraw();
      return done > 0 ? Result.Success : Result.Failure;
    }
  }

  // Turn point clouds in the document into proxies: full cloud saved as .ply next to the .3dm
  public class MegabatProxyCommand : Command
  {
    public override string EnglishName => "MegabatProxy";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
      if (string.IsNullOrEmpty(doc.Path))
      {
        RhinoApp.WriteLine("Megabat: save the Rhino file first; the full clouds are written next to it.");
        return Result.Failure;
      }
      var go = new GetObject();
      go.SetCommandPrompt("Select point clouds");
      go.GeometryFilter = ObjectType.PointSet;
      go.SubObjectSelect = false;
      go.GetMultiple(1, 0);
      if (go.CommandResult() != Result.Success) return go.CommandResult();
      int count = MegabatRhino.Instance.ProxyCount;
      if (!MegabatRhino.AskCount("Points shown in Rhino", ref count)) return Result.Cancel;
      MegabatRhino.Instance.ProxyCount = count;

      var dir = Path.Combine(Path.GetDirectoryName(doc.Path), Path.GetFileNameWithoutExtension(doc.Path) + "_clouds");
      Directory.CreateDirectory(dir);
      int done = 0;
      foreach (var r in go.Objects())
      {
        var obj = r.Object();
        if (!(obj?.Geometry is PointCloud full)) continue;
        if (CloudFile.IsProxy(obj.Attributes)) { RhinoApp.WriteLine("Megabat: {0} is already a proxy", Label(obj)); continue; }
        if (full.Count <= count) { RhinoApp.WriteLine("Megabat: {0} has only {1:N0} points, left as it is", Label(obj), full.Count); continue; }
        var file = Path.Combine(dir, Safe(Label(obj)) + ".ply");
        int k = 2;
        while (File.Exists(file)) file = Path.Combine(dir, Safe(Label(obj)) + "_" + k++ + ".ply");
        try
        {
          RhinoApp.SetCommandPrompt("Writing " + Path.GetFileName(file) + " ...");
          CloudFile.WritePly(full, file);
          var proxy = CloudUtil.Thin(full, count);
          var att = obj.Attributes.Duplicate();
          if (string.IsNullOrEmpty(att.Name)) att.Name = Label(obj);
          CloudFile.MarkProxy(att, file, doc.Path, full.Count);
          doc.Objects.Replace(obj.Id, proxy);
          doc.Objects.ModifyAttributes(obj.Id, att, true);
          RhinoApp.WriteLine(string.Format("Megabat: {0} → {1:N0} of {2:N0} points shown, full cloud in {3}", Label(obj), proxy.Count, full.Count, file));
          done++;
        }
        catch (Exception e) { RhinoApp.WriteLine("Megabat: " + Label(obj) + ": " + e.Message); }
      }
      doc.Views.Redraw();
      return done > 0 ? Result.Success : Result.Nothing;
    }

    static string Label(RhinoObject o) => string.IsNullOrEmpty(o.Attributes.Name) ? "cloud_" + o.Id.ToString("N").Substring(0, 8) : o.Attributes.Name;
    static string Safe(string s) => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
  }

  // Bring the full cloud back into the document (e.g. to snap to every point or to export)
  public class MegabatFullCommand : Command
  {
    public override string EnglishName => "MegabatFull";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
      var go = new GetObject();
      go.SetCommandPrompt("Select proxy clouds");
      go.GeometryFilter = ObjectType.PointSet;
      go.SubObjectSelect = false;
      go.GetMultiple(1, 0);
      if (go.CommandResult() != Result.Success) return go.CommandResult();
      int done = 0;
      foreach (var r in go.Objects())
      {
        var obj = r.Object();
        if (obj == null || !CloudFile.IsProxy(obj.Attributes)) continue;
        var file = CloudFile.SourceOf(obj.Attributes, doc.Path);
        if (file == null) { RhinoApp.WriteLine("Megabat: file of {0} not found ({1})", obj.Attributes.Name, obj.Attributes.GetUserString(CloudFile.KeySource)); continue; }
        try
        {
          var full = CloudFile.Load(file, doc.ModelUnitSystem);
          var att = obj.Attributes.Duplicate();
          CloudFile.Unmark(att);
          doc.Objects.Replace(obj.Id, full);
          doc.Objects.ModifyAttributes(obj.Id, att, true);
          RhinoApp.WriteLine("Megabat: {0} restored with {1:N0} points; the file {2} is kept", obj.Attributes.Name, full.Count, file);
          done++;
        }
        catch (Exception e) { RhinoApp.WriteLine("Megabat: " + e.Message); }
      }
      doc.Views.Redraw();
      return done > 0 ? Result.Success : Result.Nothing;
    }
  }
}
