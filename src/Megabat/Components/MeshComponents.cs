using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Megabat.Core;

namespace Megabat.Components
{
  public class MeshCropComponent : MegabatComponent
  {
    public MeshCropComponent() : base("MeshCrop", "MCrop",
      "Cut a large mesh at a closed Brep or at planes. Faces wholly inside or outside are copied unchanged, only the narrow border strip is split, so it is much faster than Mesh Split.", Tab.Cloud) { }
    public override Guid ComponentGuid => new Guid("a3c9f0d2-11e4-4b6a-8a01-6d2f5e00000a");
    public override GH_Exposure Exposure => GH_Exposure.tertiary;
    protected override string IconText => "MX";
    protected override Color IconColor => Icons.Cloud;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
      p.AddMeshParameter("M", "M", "Mesh; a mesh proxy from MegabatImport/MegabatProxy is read in full from its file", GH_ParamAccess.item);
      p.AddBrepParameter("B", "B", "Optional: closed Brep of the region (boxes and other planar convex Breps are fastest)", GH_ParamAccess.item);
      p.AddPlaneParameter("P", "P", "Optional, instead of B: planes; the region is where all plane normals point (e.g. two facing planes = a slab)", GH_ParamAccess.list);
      Optional(p, 1, 2);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
      p.AddMeshParameter("Inside", "In", "Part inside the region, cut exactly at its boundary", GH_ParamAccess.item);
      p.AddMeshParameter("Outside", "Out", "Rest of the mesh (only computed when connected)", GH_ParamAccess.item);
      p.AddTextParameter("Info", "Info", "Faces copied whole, faces split at the border, run time", GH_ParamAccess.item);
    }
    protected override void SolveInstance(IGH_DataAccess DA)
    {
      Brep b = null; var P = new List<Plane>();
      var m = GetMesh(DA, 0);
      if (m == null) return;
      DA.GetData(1, ref b); DA.GetDataList(2, P);
      if (P.Count == 0 && b == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Give a closed Brep B or planes P"); return; }
      if (P.Count == 0 && !b.IsSolid) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Brep is not closed"); return; }
      double tol = Rhino.RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.001;
      MeshToolsCore.CropResult r;
      try { r = MeshToolsCore.Crop(m, P.Count > 0 ? null : b, P, tol, Params.Output[1].Recipients.Count > 0); }
      catch (Exception e) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message); return; }
      DA.SetData(0, r.Inside); if (r.Outside != null) DA.SetData(1, r.Outside);
      DA.SetData(2, string.Format("{0:N0} faces: {1:N0} copied whole, {2:N0} split at the border, {3}", m.Faces.Count, r.Whole, r.Border, Ms));
    }
  }

  public class ReduceMeshComponent : MegabatComponent
  {
    public ReduceMeshComponent() : base("ReduceMesh", "Reduce",
      "Fast mesh reduction by vertex clustering on a voxel grid: seconds for millions of faces. Coarser than Rhino's Reduce, good for previews, proxies and quick analysis.", Tab.Cloud) { }
    public override Guid ComponentGuid => new Guid("a3c9f0d2-11e4-4b6a-8a01-6d2f5e00000b");
    public override GH_Exposure Exposure => GH_Exposure.tertiary;
    protected override string IconText => "Rd";
    protected override Color IconColor => Icons.Cloud;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
      p.AddMeshParameter("M", "M", "Mesh; a mesh proxy is read in full from its file", GH_ParamAccess.item);
      p.AddIntegerParameter("N", "N", "Approximate face count after reduction (only if voxel is 0)", GH_ParamAccess.item, 100000);
      p.AddNumberParameter("voxel", "voxel", "Optional: voxel edge length; if > 0 it overrides N", GH_ParamAccess.item, 0);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
      p.AddMeshParameter("Mesh", "Mesh", "Reduced mesh; vertex colours averaged per voxel", GH_ParamAccess.item);
      p.AddTextParameter("Info", "Info", "Faces before and after, voxel, run time", GH_ParamAccess.item);
    }
    protected override void SolveInstance(IGH_DataAccess DA)
    {
      int N = 100000; double voxel = 0;
      var m = GetMesh(DA, 0);
      if (m == null) return;
      DA.GetData(1, ref N); DA.GetData(2, ref voxel);
      Mesh r;
      if (voxel > 0) r = MeshCloudCore.Cluster(m, voxel);
      else r = MeshCloudCore.Cluster(m, Math.Max(1, N), out voxel);
      DA.SetData(0, r);
      DA.SetData(1, string.Format("{0:N0} → {1:N0} faces, voxel {2:0.0000}, {3}", m.Faces.Count, r?.Faces.Count ?? 0, voxel, Ms));
    }
  }

  public class MeshClosestComponent : MegabatComponent
  {
    public MeshClosestComponent() : base("MeshClosest", "MClosest",
      "Closest points on a mesh for many points or a whole cloud, on all processor cores (Mesh Closest Point runs on one).", Tab.Cloud) { }
    public override Guid ComponentGuid => new Guid("a3c9f0d2-11e4-4b6a-8a01-6d2f5e00000c");
    public override GH_Exposure Exposure => GH_Exposure.tertiary;
    protected override string IconText => "MP";
    protected override Color IconColor => Icons.Cloud;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
      p.AddMeshParameter("M", "M", "Mesh; a mesh proxy is read in full from its file", GH_ParamAccess.item);
      p.AddPointParameter("P", "P", "Optional: query points", GH_ParamAccess.list);
      AddCloud(p, "C", "Optional: query cloud (e.g. a scan to compare with a mesh)", GH_ParamAccess.item, true);
      p.AddNumberParameter("max", "max", "Search distance; points farther away get no result (distance -1). 0 = unlimited", GH_ParamAccess.item, 0);
      Optional(p, 1);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
      p.AddPointParameter("Points", "P", "Closest points on the mesh (P first, then C)", GH_ParamAccess.list);
      p.AddNumberParameter("Distance", "D", "Distances (-1 = nothing within max)", GH_ParamAccess.list);
      p.AddIntegerParameter("Face", "F", "Face index of each closest point (-1 = none)", GH_ParamAccess.list);
      p.AddTextParameter("Info", "Info", "Point count, mean and maximum distance, run time", GH_ParamAccess.item);
    }
    protected override void SolveInstance(IGH_DataAccess DA)
    {
      var P = new List<Point3d>(); Types.CloudGoo cg = null; double max = 0;
      var m = GetMesh(DA, 0);
      if (m == null) return;
      DA.GetDataList(1, P); DA.GetData(2, ref cg); DA.GetData(3, ref max);
      if (cg?.Value != null) P.AddRange(cg.Value.GetPoints());
      if (P.Count == 0) return;
      var pts = new Point3d[P.Count]; var d = new double[P.Count]; var f = new int[P.Count];
      MeshToolsCore.Closest(m, P, max, pts, d, f);
      var hit = d.Where(x => x >= 0).ToArray();
      DA.SetDataList(0, pts); DA.SetDataList(1, d); DA.SetDataList(2, f);
      DA.SetData(3, string.Format("{0:N0} points, {1:N0} found, mean {2:0.0000}, max {3:0.0000}, {4}", P.Count, hit.Length, hit.Length > 0 ? hit.Average() : 0, hit.Length > 0 ? hit.Max() : 0, Ms));
    }
  }
}
