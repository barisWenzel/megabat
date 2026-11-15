# MergeClouds

**Group:** 1 Cloud  
**Nickname:** Merge

Merge several clouds into one new cloud, e.g. the parts from CropBrep or SplitSlices.

## Inputs

| Name | Type | Default | Description |
|---|---|---|---|
| `C` | Cloud list |  | Clouds to merge |

## Outputs

| Name | Type | Description |
|---|---|---|
| `Cloud` | Cloud | Merged cloud; colours are kept if any cloud has them (others grey), normals only if all have them |
| `Info` | Text | Number of clouds and points, run time |

## How it works

The points are copied into one new cloud in parallel, so memory is briefly needed twice. For very large clouds, thin them first with Downsample. Proxies from the Rhino commands are no special case: the Cloud parameter already delivers the full cloud from the file.
