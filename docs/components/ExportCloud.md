# ExportCloud

**Group:** 1 Cloud  
**Nickname:** Export

Write clouds to files (binary PLY or XYZ text) instead of baking them into Rhino. Bring them back as light proxies with the Rhino command `MegabatImport`.

## Inputs

| Name | Type | Default | Description |
|---|---|---|---|
| `C` | Cloud list |  | Clouds to write, e.g. the slices from SplitSlices |
| `folder` | Text | | Target folder; empty = folder of the .gh file |
| `name` | Text | cloud | File name without extension; several clouds are numbered name_01, name_02, ... |
| `format` | Text | ply | ply = binary PLY (compact, full precision, colours and normals), xyz = text x y z [r g b] for other programs |
| `write` | Boolean | false | true = write the files; existing files of the same name are overwritten. Use a button or toggle |

## Outputs

| Name | Type | Description |
|---|---|---|
| `Files` | Text list | Paths of the written files |
| `Info` | Text list | Points and size per file, run time |

## How it works

Nothing is written while `write` is false; Info then lists what would be written. PLY stores coordinates as double, so georeferenced coordinates keep full precision; XYZ writes six decimals. Files are written to a temporary name first and renamed when complete.
