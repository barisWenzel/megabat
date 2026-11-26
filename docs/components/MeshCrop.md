# MeshCrop

**Group:** 1 Cloud  
**Nickname:** MCrop

Cut a large mesh at a closed Brep or at planes. Much faster than Mesh Split on meshes with millions of faces.

## Inputs

| Name | Type | Default | Description |
|---|---|---|---|
| `M` | Mesh |  | Mesh; a mesh proxy is read in full from its file |
| `B` | Brep |  | Optional: closed Brep of the region; boxes and other planar convex Breps are fastest |
| `P` | Plane list |  | Optional, instead of B: the region is where all plane normals point (two facing planes = a slab) |

## Outputs

| Name | Type | Description |
|---|---|---|
| `Inside` | Mesh | Part inside the region, cut exactly at its boundary |
| `Outside` | Mesh | Rest of the mesh, only computed when connected |
| `Info` | Text | Faces copied whole, faces split at the border, run time |

## How it works

1. Every vertex is tested against the region in parallel: against the face planes for planes and convex Breps, against a closed mesh of the Brep otherwise.
2. Faces with all vertices inside are copied whole, faces wholly outside are left out. Only the faces that may cross the boundary form a narrow border strip.
3. Only this strip is split with Rhino's mesh split, and its pieces are sorted by their centres.

Normals, vertex colours and texture coordinates are kept. On a test mesh with 2 million faces, cutting a slab between two planes takes about 0.5 s; Rhino's Mesh Split needs about 6 s per plane on half as many faces.
