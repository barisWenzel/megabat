# Georef

**Group:** 6 Photo  
**Nickname:** Georef

Transformation into a target coordinate system, e.g. from COLMAP units into the building model.

## Inputs

| Name | Type | Default | Description |
|---|---|---|---|
| `Src` | Point list |  | Points in the source system, e.g. Intersect › Pt |
| `Dst` | Point list |  | Optional: the same points in the target system (control points), same order as `Src` |
| `Len` | Number | 0 | Without `Dst`: real distance between `Src[0]` and `Src[1]`; 0 = no scaling |
| `scale` | Boolean | true | With `Dst`: false = rigid (no scaling) |

## Outputs

| Name | Type | Description |
|---|---|---|
| `Xform` | Transform | Transformation source → target |
| `Res` | Number list | With `Dst`: distance of each transformed `Src` point to its `Dst` point |
| `Scale` | Number | Scale factor |
| `Info` | Text | Mode, number of points, RMS |

## Modes

1. **Helmert (with `Dst`, at least 3 pairs).** Rotation, translation and scale in one step, least squares (quaternion method after Horn). From 4 points on, `Res` shows how well the points fit. Control points could be total station points, points picked in the laser scan or corners of an existing model. Photos and laser scan then lie in the same system.
2. **3 points (without `Dst`).** `Src[0]` becomes the origin, `Src[1]` lies on +X, `Src[2]` in the XY plane. `Len` sets the scale from a known distance. Useful for a local system, e.g. a facade laid flat for drawing.

Apply `Xform` with Grasshopper's Transform to points, curves or clouds. For [Ortho](Ortho.md), connect it to `Xform`; the photos stay in the COLMAP system.
