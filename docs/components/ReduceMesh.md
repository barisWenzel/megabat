# ReduceMesh

**Group:** 1 Cloud  
**Nickname:** Reduce

Fast mesh reduction by vertex clustering: seconds for millions of faces. Coarser than Rhino's Reduce, good for previews, proxies and quick analysis.

## Inputs

| Name | Type | Default | Description |
|---|---|---|---|
| `M` | Mesh |  | Mesh; a mesh proxy is read in full from its file |
| `N` | Integer | 100000 | Approximate face count after reduction (only if voxel is 0) |
| `voxel` | Number | 0 | Optional: voxel edge length; if > 0 it overrides N |

## Outputs

| Name | Type | Description |
|---|---|---|
| `Mesh` | Mesh | Reduced mesh; vertex colours averaged per voxel |
| `Info` | Text | Faces before and after, voxel, run time |

## How it works

The mesh is laid into a voxel grid. All vertices in one voxel are merged into their mean, triangles that collapse or repeat are dropped. With `N`, the voxel size follows from the mesh area. 2 million faces are reduced to about 100 000 in 0.2 s; Rhino's Reduce needs about 35 s for 500 000 faces. Sharp edges and thin parts get coarser than with Rhino's Reduce.
