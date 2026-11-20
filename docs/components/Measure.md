# Measure

**Group:** 6 Photo  
**Nickname:** Measure

Measures image points on the image board of [ImportColmap](ImportColmap.md) as Rhino points.

## Inputs

| Name | Type | Default | Description |
|---|---|---|---|
| `Add` | Boolean (button) | false | Measure a new point |
| `Undo` | Boolean (button) | false | Delete the last point (all its clicks) |
| `Clear` | Boolean (button) | false | Delete all measure points |
| `Photos` | Photo list |  | Optional: photos from ImportColmap, for epipolar lines and prediction |

## Outputs

| Name | Type | Description |
|---|---|---|
| `P` | Point tree | Clicks on the board, one branch per point |
| `Name` | Text list | Point name per branch |
| `Info` | Text | Number of points and clicks |

## Use

1. Press `Add`. Grasshopper is minimised.
2. Click the same feature in 2 or 3 images, then Enter (Esc cancels).
3. The clicks become Rhino points on the layer `Megabat::Measure`, named P001, P002 …, one colour per point. They can be moved, copied, renamed and deleted in Rhino; points with the same name belong together. Measure updates automatically.

With `Photos` connected:

1. after the first click the epipolar line is drawn in all other images; images that cannot see the point are greyed out,
2. the next click snaps onto that line (option `Snap` in the command line),
3. after the second click a crosshair shows the predicted position in the remaining images, and the command line shows the residual in pixels.

With snapping, two clicks intersect exactly (residual 0). A third image gives a real check.
