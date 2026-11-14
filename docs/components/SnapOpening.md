# SnapOpening

**Group:** 3 Elements  
**Nickname:** SnapOpen

Pull a roughly sketched window or door rectangle onto the opening in the wall and measure the reveal depth.

## Inputs

| Name | Type | Default | Description |
|---|---|---|---|
| `G` | Curve |  | Rough rectangle in the wall plane |
| `Seg` | Segment list |  | Segments from Segmentize or Classify (for the host wall) |
| `C` | Cloud |  | Point cloud (thinned is fine) |
| `dTol` | Number | 0.03 | Half thickness of the band around the wall plane in which points count as wall [m] |
| `search` | Number | 0.3 | Search distance around each sketched edge [m]; if no plane is closer, the wall is fitted locally from C |
| `cell` | Number | 0.03 | Grid size of the occupancy test [m], about twice the point spacing |
| `minStep` | Number | 0.2 | Minimum jump in occupancy for an edge to count as found (0 to 1) |

## Outputs

| Name | Type | Description |
|---|---|---|
| `Rect` | Curve | Snapped rectangle in the wall plane |
| `Opening` | Brep | Opening volume through the reveal |
| `Info` | Text list | Width × height, reveal depth, shift per edge against the sketch [m], host wall, run time |

## How it works

1. The host wall is the vertical segment closest to the sketched rectangle; if none lies within `search`, the wall is fitted locally from `C` around the sketch.
2. Points within `dTol` of the wall plane are written into an occupancy grid (`cell`): occupied = wall, empty = opening.
3. Each edge is moved to the jump from occupied to empty (at least `minStep`), then refined to the last wall point.
4. Points behind the wall plane inside the opening give the reveal depth (95 % quantile).
Doors work the same way; their bottom edge lies on the floor.
