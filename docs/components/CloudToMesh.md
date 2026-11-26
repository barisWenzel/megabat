# CloudToMesh

**Group:** 1 Cloud  
**Nickname:** C2M

Part of the original mesh whose vertices are still in a cloud from [MeshToCloud](MeshToCloud.md), e.g. after [CropBrep](CropBrep.md) or the points of a segment. To cut a mesh at a Brep or at planes directly, [MeshCrop](MeshCrop.md) is simpler.

## Inputs

| Name | Type | Default | Description |
|---|---|---|---|
| `M` | Mesh |  | Original mesh (the one given to MeshToCloud) |
| `C` | Cloud list |  | Clouds with the vertices to keep, one mesh per cloud |
| `all` | Boolean | true | Without B: true = keep a face only if all its vertices are in the cloud; false = one vertex is enough |
| `B` | Brep list |  | Optional: closed Breps the clouds were cropped with (same order as C, or one for all); the border is then cut exactly |

## Outputs

| Name | Type | Description |
|---|---|---|
| `Mesh` | Mesh list | One mesh per cloud; colours, normals and texture coordinates of the original are kept |
| `Info` | Text list | Vertices found and faces kept per cloud, run time |

## How it works

The vertices of the mesh are looked up in the cloud by their exact position. Without `B`, faces are only kept or dropped, so the border follows the triangles. With `B`, faces wholly inside are kept, and only the narrow border strip is split at the Brep, which gives a clean cut at a fraction of the cost of a full mesh split.

The points must keep their exact position: voxel downsampling in between moves them, and nothing is found.
