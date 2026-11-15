using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Megabat.Core;
using Megabat.Types;

namespace Megabat.Components
{
  public class DownsampleComponent : MegabatComponent
  {
    public DownsampleComponent() : base("Downsample", "Down",
      "Thin out a cloud: every k-th point (fast) or a voxel grid (one averaged point per cell), optionally to a target point count.", Tab.Cloud) { }
    public override Guid ComponentGuid => new Guid("a3c9f0d2-11e4-4b6a-8a01-6d2f5e000001");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override string IconText => "Dn";
    protected override Color IconColor => Icons.Cloud;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
      AddCloud(p, "C", "Point cloud (any size, read in blocks)");
      p.AddNumberParameter("voxel", "voxel", "Optional: voxel edge length [m]; if > 0 it overrides N", GH_ParamAccess.item, 0);
      p.AddIntegerParameter("N", "N", "Target point count (only if voxel is 0)", GH_ParamAccess.item, 500000);
      p.AddBooleanParameter("uniform", "uniform", "false = every k-th point, exactly N (fast, density stays uneven); true = voxel size is searched so that about N points remain (spatially even, ±5 %, slower)", GH_ParamAccess.item, false);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
      AddCloud(p, "Cloud", "Thinned cloud (voxel: mean per cell, colour and normal averaged)");
      p.AddTextParameter("Info", "Info", "Point count before and after, voxel size or step, run time", GH_ParamAccess.item);
    }
    protected override void SolveInstance(IGH_DataAccess DA)
    {
      CloudGoo cg = null; double voxel = 0; int N = 0; bool uniform = false;
      if (!DA.GetData(0, ref cg) || cg?.Value == null) return;
      DA.GetData(1, ref voxel); DA.GetData(2, ref N); DA.GetData(3, ref uniform);
      var C = cg.Value;
      PointCloud r; double v = 0;
      if (voxel > 0) { v = voxel; r = DownsampleCore.Run(C, v); }
      else if (N > 0 && N < C.Count)
      {
        if (uniform) { v = DownsampleCore.FindVoxel(C, N); r = DownsampleCore.Run(C, v); }
        else r = DownsampleCore.Stride(C, N);
      }
      else { AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Neither voxel nor N is smaller than the point count: cloud unchanged"); r = C; }
      DA.SetData(0, new CloudGoo(r));
      DA.SetData(1, string.Format("{0:N0} → {1:N0} points, {2}, {3}", C.Count, r.Count,
        v > 0 ? "voxel " + v.ToString("0.0000") + " m" : "every " + Math.Max(1, (int)Math.Ceiling((double)C.Count / Math.Max(1, r.Count))) + ". point", Ms));
    }
  }

  public class CropBrepComponent : MegabatComponent
  {
    public CropBrepComponent() : base("CropBrep", "Crop",
      "Cut or copy the points inside closed Breps, one cloud per Brep. Planar convex Breps (e.g. boxes) are tested especially fast.", Tab.Cloud) { }
    public override Guid ComponentGuid => new Guid("a3c9f0d2-11e4-4b6a-8a01-6d2f5e000002");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override string IconText => "Cr";
    protected override Color IconColor => Icons.Cloud;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
      AddCloud(p, "C", "Point cloud");
      p.AddBrepParameter("B", "B", "Closed Breps (crop volumes); where they overlap, a point belongs to the first Brep", GH_ParamAccess.list);
      p.AddNumberParameter("tol", "tol", "Points up to this distance outside still count as inside [m]; 0 = exact (fast)", GH_ParamAccess.item, 0);
      p.AddBooleanParameter("cut", "cut", "true = cut (Rest without the points), false = copy only (Rest = whole cloud)", GH_ParamAccess.item, true);
      p.AddBooleanParameter("merge", "merge", "true = all Breps into one cloud, false = one cloud per Brep", GH_ParamAccess.item, false);
      p.AddBooleanParameter("invert", "invert", "true = invert the selection: everything outside the Breps (one cloud)", GH_ParamAccess.item, false);
      p.AddBooleanParameter("index", "index", "true = output indices (slow for millions of points)", GH_ParamAccess.item, false);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
      AddCloud(p, "Inside", "Selected points, one cloud per Brep (one cloud with merge or invert)", GH_ParamAccess.list);
      AddCloud(p, "Rest", "Remaining points");
      p.AddIntegerParameter("Index", "Index", "Indices of all selected points (only with index = true)", GH_ParamAccess.list);
      p.AddTextParameter("Info", "Info", "Point count per Brep and rest, run time", GH_ParamAccess.item);
    }
    protected override void SolveInstance(IGH_DataAccess DA)
    {
      CloudGoo cg = null; var B = new List<Brep>(); double tol = 0; bool cut = true, merge = false, invert = false, index = false;
      if (!DA.GetData(0, ref cg) || cg?.Value == null) return;
      if (!DA.GetDataList(1, B) || B.Count == 0) return;
      DA.GetData(2, ref tol); DA.GetData(3, ref cut); DA.GetData(4, ref merge); DA.GetData(5, ref invert); DA.GetData(6, ref index);
      var C = cg.Value;
      var r = CropBrepCore.Run(C, B, Math.Max(0, tol), invert, cut, index, merge || invert);
      if (r.Warning != null) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, r.Warning);
      var ins = merge || invert ? new List<PointCloud> { r.Inside } : r.Parts;
      DA.SetDataList(0, ins.Select(c => new CloudGoo(c)));
      DA.SetData(1, new CloudGoo(cut ? r.Rest : C));
      if (index) DA.SetDataList(2, r.Index);
      DA.SetData(3, string.Format("Inside {0} points, rest {1:N0}, {2}",
        string.Join(" / ", ins.Select(c => c.Count.ToString("N0"))), cut ? C.Count - r.InCount : C.Count, Ms));
    }
  }

  public class SplitSlicesComponent : MegabatComponent
  {
    public SplitSlicesComponent() : base("SplitSlices", "Split",
      "Split a cloud into slices at one or more parallel planes, in blocks for very large clouds.", Tab.Cloud) { }
    public override Guid ComponentGuid => new Guid("a3c9f0d2-11e4-4b6a-8a01-6d2f5e000003");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override string IconText => "Sp";
    protected override Color IconColor => Icons.Cloud;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
      AddCloud(p, "C", "Point cloud");
      p.AddPlaneParameter("P", "P", "Cutting planes, parallel; order does not matter, they are sorted along the normal of the first plane", GH_ParamAccess.list);
      p.AddNumberParameter("overlap", "overlap", "Points closer than overlap to a plane go into both neighbouring slices [m]", GH_ParamAccess.item, 0);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
      AddCloud(p, "Clouds", "Slices from bottom to top (count = planes + 1)", GH_ParamAccess.list);
      p.AddTextParameter("Info", "Info", "Points per slice, run time", GH_ParamAccess.item);
    }
    protected override void SolveInstance(IGH_DataAccess DA)
    {
      CloudGoo cg = null; var P = new List<Plane>(); double overlap = 0;
      if (!DA.GetData(0, ref cg) || cg?.Value == null) return;
      if (!DA.GetDataList(1, P)) return;
      DA.GetData(2, ref overlap);
      var pl = P.Where(x => x.IsValid).ToList();
      if (pl.Count == 0) return;
      var r = SplitSlicesCore.Run(cg.Value, pl, Math.Max(0, overlap), out string warn);
      if (warn != null) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warn);
      DA.SetDataList(0, r.Clouds.Select(c => new CloudGoo(c)));
      DA.SetData(1, string.Format("{0} slices: {1} points, {2}", r.Clouds.Count, string.Join(" / ", r.Clouds.Select(c => c.Count.ToString("N0"))), Ms));
    }
  }

  public class ExportCloudComponent : MegabatComponent
  {
    public ExportCloudComponent() : base("ExportCloud", "Export",
      "Write clouds to files (binary PLY or XYZ text) instead of baking them into Rhino. Bring them back as light proxies with the Rhino command MegabatImport.", Tab.Cloud) { }
    public override Guid ComponentGuid => new Guid("a3c9f0d2-11e4-4b6a-8a01-6d2f5e000004");
    public override GH_Exposure Exposure => GH_Exposure.secondary;
    protected override string IconText => "Ex";
    protected override Color IconColor => Icons.Cloud;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
      AddCloud(p, "C", "Clouds to write, e.g. the slices from SplitSlices", GH_ParamAccess.list);
      int f = p.AddTextParameter("folder", "folder", "Target folder; empty = folder of the .gh file", GH_ParamAccess.item);
      p[f].Optional = true;
      p.AddTextParameter("name", "name", "File name without extension; several clouds are numbered name_01, name_02, ...", GH_ParamAccess.item, "cloud");
      p.AddTextParameter("format", "format", "ply = binary PLY (compact, full precision, colours and normals), xyz = text x y z [r g b] for other programs", GH_ParamAccess.item, "ply");
      p.AddBooleanParameter("write", "write", "true = write the files; existing files of the same name are overwritten. Use a button or toggle", GH_ParamAccess.item, false);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
      p.AddTextParameter("Files", "Files", "Paths of the written files", GH_ParamAccess.list);
      p.AddTextParameter("Info", "Info", "Points and size per file, run time", GH_ParamAccess.list);
    }
    protected override void SolveInstance(IGH_DataAccess DA)
    {
      var C = new List<CloudGoo>(); string folder = null, name = "cloud", format = "ply"; bool write = false;
      if (!DA.GetDataList(0, C)) return;
      DA.GetData(1, ref folder); DA.GetData(2, ref name); DA.GetData(3, ref format); DA.GetData(4, ref write);
      var clouds = C.Where(c => c?.Value != null && c.Value.Count > 0).Select(c => c.Value).ToList();
      if (clouds.Count == 0) return;
      format = (format ?? "ply").Trim().TrimStart('.').ToLowerInvariant();
      if (format != "ply" && format != "xyz") { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "format must be ply or xyz"); return; }
      if (string.IsNullOrWhiteSpace(folder))
      {
        var gh = OnPingDocument()?.FilePath;
        if (string.IsNullOrEmpty(gh)) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Give a folder or save the .gh file first"); return; }
        folder = Path.GetDirectoryName(gh);
      }
      if (string.IsNullOrWhiteSpace(name)) name = "cloud";
      name = string.Concat(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
      int digits = Math.Max(2, clouds.Count.ToString().Length);
      var files = new List<string>();
      for (int i = 0; i < clouds.Count; i++)
        files.Add(Path.Combine(folder, clouds.Count == 1 ? name + "." + format : name + "_" + (i + 1).ToString().PadLeft(digits, '0') + "." + format));
      if (!write)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "write = false: nothing written");
        DA.SetDataList(1, files.Select((f, i) => string.Format("would write {0:N0} points to {1}", clouds[i].Count, f)));
        return;
      }
      var info = new List<string>();
      try
      {
        Directory.CreateDirectory(folder);
        for (int i = 0; i < clouds.Count; i++)
        {
          if (format == "ply") CloudFile.WritePly(clouds[i], files[i]); else CloudFile.WriteXyz(clouds[i], files[i]);
          info.Add(string.Format("{0}: {1:N0} points, {2:0.0} MB", Path.GetFileName(files[i]), clouds[i].Count, new FileInfo(files[i]).Length / 1e6));
        }
      }
      catch (Exception e) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message); }
      info.Add(Ms);
      DA.SetDataList(0, files.Where(File.Exists));
      DA.SetDataList(1, info);
    }
  }
}
