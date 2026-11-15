# ImportCloud

**Group:** 1 Cloud  
**Nickname:** Import

Read point cloud files directly into Grasshopper, without a Rhino object. The .gh file only stores the path.

## Inputs

| Name | Type | Default | Description |
|---|---|---|---|
| `F` | Text list |  | File paths (.e57 .ply .3dm .xyz .pts .txt .csv), e.g. from a File Path parameter |
| `load` | Boolean | true | false = nothing is read (e.g. while choosing files) |

## Outputs

| Name | Type | Description |
|---|---|---|
| `Cloud` | Cloud list | One cloud per file |
| `Info` | Text list | Points per file, run time |

## How it works

The files are read with the same reader as the Rhino command `MegabatImport`. E57 files are read with the E57 importer built into Rhino 8 (all scans merged, scaled from metres into the document units); PLY and text files carry no units and are taken as they are. Files are cached, so they are read only once while they do not change.

The Rhino document stays empty; only the thinned Grasshopper preview is drawn. Rhino snapping does not find these points; use `MegabatImport` for that.
