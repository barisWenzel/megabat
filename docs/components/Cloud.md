# Cloud (parameter)

**Group:** 1 Cloud

Point cloud parameter for Megabat. Use it like the Grasshopper Point Cloud parameter, but for large clouds.

## Usage

1. Right click › *Set one Point Cloud* and pick a point cloud in Rhino. Only the reference (object ID) is stored in the .gh file.
2. Or internalise a cloud. It is then stored completely in the .gh file, which makes the file large.
3. Connect it to any Megabat component, e.g. Downsample.

If the referenced object is a proxy made with `MegabatImport` or `MegabatProxy` (see README), the parameter loads the full cloud from its file instead of the thinned proxy.

## Why not the normal Point Cloud parameter?

| | Grasshopper Point Cloud | Megabat Cloud |
|---|---|---|
| Referencing from Rhino | copies the cloud | uses the Rhino object directly |
| Passing to the next parameter | copies the cloud | passes the same cloud |
| Preview | draws every point | draws a cached, thinned copy (PreviewGuard limit) |

Megabat components never modify an input cloud; they always create new clouds for their outputs. This is why passing without copying is safe.
