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
      int f = p.AddTextParameter("folder", "folder", "Target folder; empty = folder of the .gh file; relative paths start there", GH_ParamAccess.item);
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
      if (!string.IsNullOrWhiteSpace(folder)) folder = Resolve(folder);
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

  public class ImportCloudComponent : MegabatComponent
  {
    public ImportCloudComponent() : base("ImportCloud", "Import",
      "Read point cloud files directly into Grasshopper, without a Rhino object. The .gh file only stores the path; files are cached.", Tab.Cloud) { }
    public override Guid ComponentGuid => new Guid("a3c9f0d2-11e4-4b6a-8a01-6d2f5e000006");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override string IconText => "Im";
    protected override Color IconColor => Icons.Cloud;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
      p.AddTextParameter("F", "F", "File paths (.e57 .ply .3dm .xyz .pts .txt .csv), e.g. from a File Path parameter; relative paths start at the folder of the .gh file", GH_ParamAccess.list);
      p.AddBooleanParameter("load", "load", "true = read the files; false = nothing is read (e.g. while choosing files)", GH_ParamAccess.item, true);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
      AddCloud(p, "Cloud", "One cloud per file; E57 scaled from metres into the document units, other formats taken as they are", GH_ParamAccess.list);
      p.AddTextParameter("Info", "Info", "Points per file, run time", GH_ParamAccess.list);
    }
    protected override void SolveInstance(IGH_DataAccess DA)
    {
      var F = new List<string>(); bool load = true;
      if (!DA.GetDataList(0, F)) return;
      DA.GetData(1, ref load);
      if (!load) { AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "load = false: nothing read"); return; }
      var units = Rhino.RhinoDoc.ActiveDoc?.ModelUnitSystem ?? Rhino.UnitSystem.None;
      var outC = new List<CloudGoo>(); var info = new List<string>();
      foreach (var f in F)
      {
        if (string.IsNullOrWhiteSpace(f)) continue;
        var path = Resolve(f);
        if (!File.Exists(path)) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "File not found: " + path); continue; }
        try
        {
          var sw = System.Diagnostics.Stopwatch.StartNew();
          var pc = CloudFile.Load(path, units);
          outC.Add(new CloudGoo(pc, path));
          info.Add(string.Format("{0}: {1:N0} points, {2:0.0} s", Path.GetFileName(path), pc.Count, sw.Elapsed.TotalSeconds));
        }
        catch (Exception e) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, Path.GetFileName(path) + ": " + e.Message); }
      }
      DA.SetDataList(0, outC);
      DA.SetDataList(1, info);
    }
  }

  public class MergeCloudsComponent : MegabatComponent
  {
    public MergeCloudsComponent() : base("MergeClouds", "Merge",
      "Merge several clouds into one new cloud, e.g. the parts from CropBrep or SplitSlices.", Tab.Cloud) { }
    public override Guid ComponentGuid => new Guid("a3c9f0d2-11e4-4b6a-8a01-6d2f5e000005");
    public override GH_Exposure Exposure => GH_Exposure.secondary;
    protected override string IconText => "Mg";
    protected override Color IconColor => Icons.Cloud;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
      AddCloud(p, "C", "Clouds to merge", GH_ParamAccess.list);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
      AddCloud(p, "Cloud", "Merged cloud; colours are kept if any cloud has them (others grey), normals only if all have them");
      p.AddTextParameter("Info", "Info", "Number of clouds and points, run time", GH_ParamAccess.item);
    }
    protected override void SolveInstance(IGH_DataAccess DA)
    {
      var C = new List<CloudGoo>();
      if (!DA.GetDataList(0, C)) return;
      var clouds = C.Where(c => c?.Value != null && c.Value.Count > 0).Select(c => c.Value).ToList();
      if (clouds.Count == 0) return;
      PointCloud r;
      try { r = clouds.Count == 1 ? clouds[0] : CloudUtil.Merge(clouds); }
      catch (Exception e) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message); return; }
      DA.SetData(0, new CloudGoo(r));
      DA.SetData(1, string.Format("{0} clouds → {1:N0} points, {2}", clouds.Count, r.Count, Ms));
    }
  }

  public class MeshToCloudComponent : MegabatComponent
  {
    public MeshToCloudComponent() : base("MeshToCloud", "M2C",
      "Mesh vertices as a cloud (colour and normal per vertex), so that all cloud tools also work on meshes. CloudToMesh takes the faces back from the original mesh.", Tab.Cloud) { }
    public override Guid ComponentGuid => new Guid("a3c9f0d2-11e4-4b6a-8a01-6d2f5e000008");
    public override GH_Exposure Exposure => GH_Exposure.secondary;
    protected override string IconText => "MC";
    protected override Color IconColor => Icons.Cloud;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
      p.AddMeshParameter("M", "M", "Mesh, e.g. from photogrammetry; a mesh proxy from MegabatImport/MegabatProxy is read in full from its file", GH_ParamAccess.item);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
      AddCloud(p, "Cloud", "One point per vertex; vertex colours if present, normals always");
      p.AddTextParameter("Info", "Info", "Vertex and face count, run time", GH_ParamAccess.item);
    }
    protected override void SolveInstance(IGH_DataAccess DA)
    {
      var m = GetMesh(DA, 0);
      if (m == null) return;
      var pc = MeshCloudCore.ToCloud(m);
      DA.SetData(0, new CloudGoo(pc));
      DA.SetData(1, string.Format("{0:N0} vertices, {1:N0} faces, {2}", pc.Count, m.Faces.Count, Ms));
    }
  }

  public class CloudToMeshComponent : MegabatComponent
  {
    public CloudToMeshComponent() : base("CloudToMesh", "C2M",
      "Part of the original mesh whose vertices are still in a cloud from MeshToCloud, e.g. after CropBrep or a section. Faster than splitting the mesh. Points must keep their exact position (no voxel downsampling).", Tab.Cloud) { }
    public override Guid ComponentGuid => new Guid("a3c9f0d2-11e4-4b6a-8a01-6d2f5e000009");
    public override GH_Exposure Exposure => GH_Exposure.secondary;
    protected override string IconText => "CM";
    protected override Color IconColor => Icons.Cloud;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
      p.AddMeshParameter("M", "M", "Original mesh (the one given to MeshToCloud)", GH_ParamAccess.item);
      AddCloud(p, "C", "Cloud with the vertices to keep", GH_ParamAccess.list);
      p.AddBooleanParameter("all", "all", "Without B: true = keep a face only if all its vertices are in the cloud; false = one vertex is enough", GH_ParamAccess.item, true);
      p.AddBrepParameter("B", "B", "Optional: closed Breps the clouds were cropped with (same order as C, or one for all); the border faces are then cut exactly at the Brep, only the narrow border strip is split", GH_ParamAccess.list);
      p[3].Optional = true;
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
      p.AddMeshParameter("Mesh", "Mesh", "One mesh per cloud; colours, normals and texture coordinates of the original are kept", GH_ParamAccess.list);
      p.AddTextParameter("Info", "Info", "Vertices found and faces kept per cloud, run time", GH_ParamAccess.list);
    }
    protected override void SolveInstance(IGH_DataAccess DA)
    {
      var C = new List<CloudGoo>(); bool all = true; var B = new List<Brep>();
      var m = GetMesh(DA, 0);
      if (m == null) return;
      if (!DA.GetDataList(1, C)) return;
      DA.GetData(2, ref all);
      DA.GetDataList(3, B);
      double tol = Rhino.RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.001;
      var outM = new List<Mesh>(); var info = new List<string>();
      for (int ci = 0; ci < C.Count; ci++)
      {
        var cg = C[ci]; int kept;
        var b = B.Count == 0 ? null : B[Math.Min(ci, B.Count - 1)];
        if (b != null && !b.IsSolid) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Brep is not closed: faces removed without cutting"); b = null; }
        var r = b != null ? MeshCloudCore.ExtractCut(m, cg?.Value, b, tol, out kept) : MeshCloudCore.Extract(m, cg?.Value, all, out kept);
        outM.Add(r);
        info.Add(r == null ? string.Format("{0:N0} vertices found, no face", kept)
                           : string.Format("{0:N0} vertices found, {1:N0} of {2:N0} faces", kept, r.Faces.Count, m.Faces.Count));
        if (cg?.Value != null && kept == 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No vertex of the mesh found in a cloud: was it made with MeshToCloud from this mesh, without voxel downsampling?");
      }
      if (info.Count > 0) info[info.Count - 1] += ", " + Ms;
      DA.SetDataList(0, outM);
      DA.SetDataList(1, info);
    }
  }

  public class ViewPointsComponent : MegabatComponent
  {
    public ViewPointsComponent() : base("ViewPoints", "Views",
      "Standpoints that see as much of the surfaces as possible, e.g. camera positions for Detect or scanner positions. Occupancy grid with normals, candidates at a height above the floor, line of sight with distance and incidence angle, greedy choice.", Tab.Cloud) { }
    public override Guid ComponentGuid => new Guid("a3c9f0d2-11e4-4b6a-8a01-6d2f5e000007");
    public override GH_Exposure Exposure => GH_Exposure.secondary;
    protected override string IconText => "VP";
    protected override Color IconColor => Icons.Cloud;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
      AddCloud(p, "C", "Point cloud");
      p.AddNumberParameter("voxel", "voxel", "Voxel size of the occupancy grid [m]", GH_ParamAccess.item, 0.1);
      p.AddNumberParameter("height", "height", "Height of the standpoints above the floor [m]", GH_ParamAccess.item, 1.5);
      p.AddNumberParameter("step", "step", "Spacing of the candidate grid [m]", GH_ParamAccess.item, 0.5);
      p.AddNumberParameter("clear", "clear", "Free space around a standpoint [m]", GH_ParamAccess.item, 0.3);
      p.AddNumberParameter("dMin", "dMin", "Minimum distance (blind spot) [m]", GH_ParamAccess.item, 0.5);
      p.AddNumberParameter("dMax", "dMax", "Maximum distance [m]", GH_ParamAccess.item, 15);
      p.AddNumberParameter("angle", "angle", "Maximum incidence angle to the surface normal [°]", GH_ParamAccess.item, 70);
      p.AddNumberParameter("cover", "cover", "Stop when this share of the surface voxels is seen (0 to 1)", GH_ParamAccess.item, 0.95);
      p.AddIntegerParameter("max", "max", "Maximum number of standpoints", GH_ParamAccess.item, 5);
      p.AddBooleanParameter("room", "room", "true = only candidates below a ceiling (indoors); false = also outdoors", GH_ParamAccess.item, true);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
      p.AddPointParameter("Pts", "Pts", "Standpoints in the order chosen, e.g. for Detect › cam", GH_ParamAccess.list);
      p.AddNumberParameter("Cover", "Cover", "Cumulative share of the surface voxels seen after each standpoint", GH_ParamAccess.list);
      AddCloud(p, "Seen", "Voxels first seen from each standpoint (one cloud per standpoint)", GH_ParamAccess.list);
      int c = p.AddPointParameter("Cand", "Cand", "All candidate positions", GH_ParamAccess.list);
      p.AddTextParameter("Info", "Info", "Voxels, candidates, standpoints, coverage, run time", GH_ParamAccess.item);
      if (p[c] is IGH_PreviewObject po) po.Hidden = true;
    }
    protected override void SolveInstance(IGH_DataAccess DA)
    {
      CloudGoo cg = null; var o = new ViewCore.Options();
      if (!DA.GetData(0, ref cg) || cg?.Value == null) return;
      DA.GetData(1, ref o.Voxel); DA.GetData(2, ref o.Height); DA.GetData(3, ref o.Step); DA.GetData(4, ref o.Clear);
      DA.GetData(5, ref o.DMin); DA.GetData(6, ref o.DMax); DA.GetData(7, ref o.Angle); DA.GetData(8, ref o.Cover);
      DA.GetData(9, ref o.Max); DA.GetData(10, ref o.Room);
      if (o.Voxel <= 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "voxel must be > 0"); return; }
      ViewCore.Result r;
      try { r = ViewCore.Run(cg.Value, o); }
      catch (ArgumentException e) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message); return; }
      if (r.Candidates.Count == 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "no candidates: check height, clear, step or switch room off");
      DA.SetDataList(0, r.Points); DA.SetDataList(1, r.Cover);
      var seen = new List<CloudGoo>();
      for (int i = 0; i < r.Seen.Count; i++) { var pc = new PointCloud(r.Seen[i]); seen.Add(new CloudGoo(pc)); }
      DA.SetDataList(2, seen); DA.SetDataList(3, r.Candidates);
      DA.SetData(4, string.Format("{0:N0} surface voxels, {1} candidates, {2} standpoints, coverage {3:0} % ({4}); {5}",
        r.Voxels, r.Candidates.Count, r.Points.Count, r.Cover.Count > 0 ? r.Cover[r.Cover.Count - 1] * 100 : 0,
        string.Join(" ", r.Cover.Select(x => (x * 100).ToString("0") + "%")), Ms));
    }
  }
}
