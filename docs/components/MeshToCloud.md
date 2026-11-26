# MeshToCloud

**Group:** 1 Cloud  
**Nickname:** M2C

Mesh vertices as a cloud, so that all cloud tools also work on meshes, e.g. photogrammetry meshes. [CloudToMesh](CloudToMesh.md) takes the faces back from the original mesh.

## Inputs

| Name | Type | Default | Description |
|---|---|---|---|
| `M` | Mesh |  | Mesh; a mesh proxy from `MegabatImport` / `MegabatProxy` is read in full from its file |

## Outputs

| Name | Type | Description |
|---|---|---|
| `Cloud` | Cloud | One point per vertex; vertex colours if present, normals always |
| `Info` | Text | Vertex and face count, run time |

## How it works

Every vertex becomes a point with its colour and normal (normals are computed if the mesh has none). The positions are copied exactly, so CloudToMesh can find the vertices again after cropping or sectioning the cloud. One million vertices take well below a second.

Photogrammetry meshes are often dense at details and coarse on flat walls. Tools that work on a raster of the points (outlines, openings) may then see holes where the triangles are large.
