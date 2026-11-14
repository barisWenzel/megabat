# Section

**Group:** 4 Section  
**Nickname:** Section

Architectural section through the cloud at any plane: cut lines from the segment planes, the rest traced or fitted by RANSAC; plus the view beyond.

## Inputs

| Name | Type | Default | Description |
|---|---|---|---|
| `C` | Cloud |  | Point cloud (any size, read in blocks) |
| `P` | Plane |  | Section plane; plan = horizontal plane, longitudinal/cross section = vertical plane |
| `thick` | Number | 0.04 | Thickness of the section band [m] |
| `depth` | Number | 0 | Depth of the view [m]: positive = along the normal of P, negative = opposite, 0 = none |
| `Seg` | Segment list |  | Optional: segments; each plane is intersected with the section plane and trimmed by its points |
| `tol` | Number | 0.01 | Line fit: max. distance of a point to the line [m] |
| `minLen` | Number | 0.1 | Shortest line [m] |
| `gap` | Number | 0.15 | Gaps up to this length are bridged, corners up to this distance are joined [m] |
| `trace` | Boolean | true | true = chain the points like by hand (polylines), false = straight lines by RANSAC |

## Outputs

| Name | Type | Description |
|---|---|---|
| `Cut` | Curve list | Cut lines: first from the planes, then the rest traced or fitted by RANSAC |
| `CutPts` | Cloud | Points of the section band, projected into the plane |
| `Beyond` | Cloud | Points beyond the section up to depth, in original position |
| `Info` | Text | Points in band and view, number of lines, unused points, run time |

## How it works

1. Points within `thick/2` of the section plane form the band, points beyond it up to `depth` form the view (in original position).
2. If segments are given, each segment plane is intersected with the section plane and trimmed to its points in the band. Band points explained by these lines are removed.
3. The remaining band points are traced like by hand (`trace = true`, polylines) or fitted with 2D RANSAC lines (`trace = false`).
4. Line ends closer than `gap` are joined at their intersection; pieces shorter than `minLen` are dropped.
