# SegmentRect

**Group:** 3 Elements  
**Nickname:** SegRect

One rectangular surface per segment. Walls are oriented horizontal/vertical, inclined faces along their horizontal line (eaves), floors and ceilings along the wall direction. Edges are trimmed to the neighbouring planes, so walls meet floor, ceiling and each other.

## Inputs

| Name | Type | Default | Description |
|---|---|---|---|
| `Seg` | Segment list |  | Segments from Segmentize or Classify |
| `Dir` | Vector |  | Optional: main direction for floors and ceilings, e.g. Dir from Classify; otherwise taken from the walls, without walls the minimum-area rectangle per face |
| `q` | Number | 1 | Ignored outliers per side [%]; 0 = extreme points |
| `trim` | Boolean | true | Move edges to the intersection line with a neighbouring plane |
| `gap` | Number | 0.3 | Max. distance between edge and neighbour plane for trimming [m] |
| `angTol` | Number | 10 | Max. angle between edge and intersection line [°] |
| `sample` | Integer | 20000 | Points used per segment |
| `cell` | Number | 0 | Raster size for the outline [m]; 0 = no outline |
| `close` | Integer | 2 | Outline: cells for closing gaps between points |

## Outputs

| Name | Type | Description |
|---|---|---|
| `Srf` | Brep list | Rectangular surface per segment |
| `Rect` | Curve list | Outline per segment |
| `Cov` | Number list | Coverage: share of the rectangle covered by points (0 to 1) |
| `Info` | Text list | Per segment: size, orientation rule, trimmed edges with neighbour segment, coverage; run time |
| `Shape` | Brep tree | Only with `cell` > 0: rectangle cut to the outline of the points (L-shapes, openings as holes), one branch per segment |

## How it works

1. **Orientation.** Walls use the horizontal line in the plane and the vertical. Inclined faces use their horizontal line. Floors and ceilings use `Dir` or, if not given, the main wall direction (wall normals averaged modulo 90°, weighted by point count); without walls the minimum-area rectangle (rotating calipers on the convex hull).
2. **Extent.** The points are projected into the plane; the rectangle spans the `q` and 100 − `q` percentiles along both axes, so single outliers do not inflate it.
3. **Trimming.** Each edge is moved to the intersection line with the nearest neighbouring plane that runs parallel to it (within `angTol`), lies within `gap` and actually reaches that line.
4. **Coverage.** A 40 × 40 raster over the rectangle shows how much of it is covered by points.
5. **Outline (optional).** With `cell` > 0 the points are rasterised in the rectangle, small gaps are closed (dilate and erode by `close` cells), gaps up to `gap` between points and rectangle edge are filled, and the cell edges are chained into loops. The result in `Shape` follows L-shapes and keeps openings as holes.

A low `Cov` points to L-shaped faces, openings or occlusion; `Shape` handles those.
