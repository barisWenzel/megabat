# SnapPolygon

**Group:** 3 Elements  
**Nickname:** SnapPoly

Pull a roughly sketched room outline or wall run onto the walls of the cloud and output it as a room volume or wall surface.

## Inputs

| Name | Type | Default | Description |
|---|---|---|---|
| `G` | Curve |  | Rough outline as polyline (plan view); closed = room, open see open |
| `Seg` | Segment list |  | Segments from Segmentize or Classify |
| `C` | Cloud |  | Optional: point cloud for edges without a matching plane; a line is fitted locally into the wall points there |
| `zBot` | Number |  | Optional: bottom [m]; otherwise lowest horizontal plane inside the outline, otherwise bottom of the largest segment |
| `zTop` | Number |  | Optional: top [m]; otherwise highest horizontal plane inside the outline, otherwise top of the largest segment |
| `open` | Boolean | false | true = open wall run: no closing edge, Room is the wall surface without caps |
| `dTol` | Number | 0.3 | Max. distance between polygon edge and wall [m] |
| `angTol` | Number | 10 | Max. angle between polygon edge and wall [°] |
| `minSup` | Number | 0.3 | Minimum share of the edge length that must be covered by wall points |
| `sample` | Integer | 5000 | Sample size per wall for the coverage test |

## Outputs

| Name | Type | Description |
|---|---|---|
| `Polygon` | Curve | Snapped outline at floor level |
| `Room` | Brep | Room volume between bottom and top; with open only the wall surface |
| `Info` | Text list | Per edge: assigned wall (segment number, fitted locally or none), distance of the sketch, coverage, plumb deviation top minus bottom and flatness (5 to 95 %) [m]; heights, run time |

## How it works

1. Every edge of the sketch is matched to a vertical segment: angle below `angTol`, distance below `dTol`, and at least `minSup` of the edge covered by wall points.
2. Edges without a matching plane are fitted locally from `C` (peak of a distance histogram, direction by PCA in a narrow band), if `C` is given.
3. Each matched wall is re-fitted from all its points in a band around the edge (outliers removed via median and MAD). Plumb deviation (top minus bottom) and flatness (5 to 95 % spread) are reported in Info.
4. Corners are the intersections of neighbouring lines; parallel neighbours get a connecting piece.
5. Floor and ceiling heights come from `zBot`/`zTop`, otherwise from the lowest and highest horizontal segment inside the outline, otherwise from the extent of the largest segment. The polygon is extruded between them (open: one vertical face per edge).
