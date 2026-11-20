# Intersect

**Group:** 6 Photo  
**Nickname:** Intersect

Forward intersection: one 3D point per branch of image clicks from [Measure](Measure.md), least squares (the point closest to all rays).

## Inputs

| Name | Type | Description |
|---|---|---|
| `P` | Point tree | Clicks on the board, one branch per point (Measure › P) |
| `Photos` | Photo list | Photos from ImportColmap |

## Outputs

| Name | Type | Description |
|---|---|---|
| `Pt` | Point list | 3D point per branch; invalid if fewer than 2 rays |
| `Rays` | Line tree | Lines camera → point, one branch per point |
| `Res` | Number tree | Distance of each ray to the point |
| `Angle` | Number list | Largest intersection angle between two rays [°]; small angles give poor depth |
| `Info` | Text | Number of points and rays, problems per branch |

The result is in COLMAP units, i.e. without scale and in an arbitrary position. [Georef](Georef.md) gives scale and position.
