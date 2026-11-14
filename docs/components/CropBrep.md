# CropBrep

**Group:** 1 Cloud  
**Nickname:** Crop

Cut or copy the points inside closed Breps, one cloud per Brep. Planar convex Breps (e.g. boxes) are tested especially fast.

## Inputs

| Name | Type | Default | Description |
|---|---|---|---|
| `C` | Cloud |  | Point cloud |
| `B` | Brep list |  | Closed Breps (crop volumes); where they overlap, a point belongs to the first Brep |
| `tol` | Number | 0 | Points up to this distance outside still count as inside [m]; 0 = exact (fast) |
| `cut` | Boolean | true | true = cut (Rest without the points), false = copy only (Rest = whole cloud) |
| `merge` | Boolean | false | true = all Breps into one cloud, false = one cloud per Brep |
| `invert` | Boolean | false | true = invert the selection: everything outside the Breps (one cloud) |
| `index` | Boolean | false | true = output indices (slow for millions of points) |

## Outputs

| Name | Type | Description |
|---|---|---|
| `Inside` | Cloud list | Selected points, one cloud per Brep (one cloud with merge or invert) |
| `Rest` | Cloud | Remaining points |
| `Index` | Integer list | Indices of all selected points (only with index = true) |
| `Info` | Text | Point count per Brep and rest, run time |

## How it works

Each point gets the index of the first Brep that contains it. Planar convex Breps (boxes, prisms) are tested against their face planes, which is very fast; all other Breps are meshed and tested with a point-in-mesh test, pre-filtered by bounding box.
By default you get one cloud per Brep in input order, without grafting. `merge` combines them, `invert` returns everything outside all Breps.
