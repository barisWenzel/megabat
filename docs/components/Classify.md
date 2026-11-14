# Classify

**Group:** 2 Segments  
**Nickname:** Classify

Sort segments by orientation (horizontal, vertical, inclined) and optionally snap them to Z or to the main wall direction.

## Inputs

| Name | Type | Default | Description |
|---|---|---|---|
| `Seg` | Segment list |  | Segments from Segmentize |
| `angTol` | Number | 10 | Angle tolerance for horizontal/vertical and for snapping [°] |
| `snap` | Boolean | true | true = snap normals to Z or to the main wall direction |

## Outputs

| Name | Type | Description |
|---|---|---|
| `Segments` | Segment list | Segments with type, planes snapped if requested |
| `Dir` | Vector | Main wall direction (mean modulo 90°) |
| `Info` | Text | Count per type, run time |

## How it works

Each plane normal is compared with Z: within `angTol` of Z it is horizontal, within `angTol` of the XY plane it is vertical, otherwise inclined. With `snap = true` horizontal normals are set exactly to Z, and vertical normals to the main wall direction (mean of all wall directions modulo 90°) or its perpendicular.
