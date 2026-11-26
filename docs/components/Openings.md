# Openings (experimental)

**Group:** 3 Elements  
**Nickname:** Openings

Windows and doors from the holes in the walls: glass and open doors return no points, so they are empty regions in the wall.

## Inputs

| Name | Type | Default | Description |
|---|---|---|---|
| `Seg` | Segment list |  | Segments from [Classify](Classify.md); the walls are used |
| `C` | Cloud |  | Optional: all points, to see through floor-level openings; without C they stay `floor opening` |
| `Dir` | Vector |  | Optional: main direction (Classify › Dir) |
| `cell` | Number | 0.05 | Raster size [m] |
| `minW` | Number | 0.4 | Minimum width [m] |
| `minH` | Number | 0.4 | Minimum height [m] |
| `sill` | Number | 0.3 | Openings starting higher than this above the bottom of the wall are windows [m] |
| `depth` | Number | 1.5 | How far to look through the opening on both sides [m] |
| `dense` | Number | 100 | Points per m² on both sides from which a floor-level opening counts as door; below a tenth of it as full-height window |

## Outputs

| Name | Type | Description |
|---|---|---|
| `Rect` | Curve list | Opening rectangles in the wall plane |
| `Type` | Text list | `window`, `door`, `full-height window` or `floor opening` (unclear) |
| `Width` | Number list | Width [m] |
| `Height` | Number list | Height [m] |
| `Sill` | Number list | Sill height above the bottom of the wall [m]; 0 = floor-level |
| `Behind` | Number list | Points per m² of opening on the less dense side |
| `Wall` | Integer list | Index of the wall segment |
| `Info` | Text | Number of openings per type, run time |

## How it works

1. Each wall gets a rectangle (as in [SegmentRect](SegmentRect.md)) and an occupancy raster of its points; small gaps between scan lines are closed.
2. Empty regions that do not touch the sides or the top of the wall and are roughly rectangular become openings.
3. Openings starting higher than `sill` are windows. For floor-level openings the points in front of and behind the opening are counted: points on both sides mean a door (one sees into the next room), nothing behind means a full-height window.

Known limits: furniture in front of a wall also leaves holes; an opening behind which nothing was scanned stays `floor opening`.
