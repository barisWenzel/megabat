# Ortho

**Group:** 6 Photo  
**Nickname:** Ortho

Orthophoto of a planar face, e.g. a facade or a window: a plane is fitted through the corner points, every pixel on it is projected into the photo that sees the face best, and the colour is read there.

## Inputs

| Name | Type | Default | Description |
|---|---|---|---|
| `Pts` | Point list |  | Corner points of the face (at least 3), e.g. Intersect › Pt or after Georef |
| `Photos` | Photo list |  | Photos from ImportColmap |
| `Xform` | Transform |  | Optional: Georef › Xform, if `Pts` are in the target system |
| `px` | Number |  | Pixel size in the units of `Pts`; empty = longest side 2000 px |
| `photo` | Integer | −1 | Index of the photo to use; −1 = automatic |
| `sort` | Boolean | true | Order the corners by angle (convex outlines); false = given order |
| `file` | Text |  | PNG file; empty = temp folder |
| `bake` | Boolean | false | Add the image as picture frame on `Megabat::Ortho` (embedded in the .3dm) |

## Outputs

| Name | Type | Description |
|---|---|---|
| `Image` | Text | Path of the PNG, transparent outside the face |
| `Frame` | Rectangle | Rectangle of the image in the units of `Pts` |
| `Srf` | Brep | Planar face from the corner points |
| `Photo` | Text | Main photo |
| `Info` | Text | Size, pixel size, plane deviation (RMS), share of the face seen, photos used |

## How it works

1. **Plane.** Least-squares plane through `Pts`; X along the edge from the first to the next corner, Y in the plane. The RMS distance of the corners to the plane is shown in `Info`.
2. **Main photo.** Among the photos that see all corners, the one with the most pixels per unit at the centre (close and frontal).
3. **Raster.** For every pixel inside the outline the 3D point is projected into the main photo (bilinear sampling). Where the main photo does not see the face, the best photo for that pixel fills the gap.
4. **Output.** PNG with transparency, textured preview, optional picture frame.

With `Xform` the corners are taken in the target system (e.g. millimetres) and transformed back into the COLMAP system for the projection, so the image comes out to scale and in place.

## Limits

1. Only the plane is rectified: projecting parts (cornices, sills, balconies) are distorted and offset.
2. Occlusion is not checked; trees or people in front of the face appear in the image.
3. A pixel size below the ground resolution of the photo gives a larger, but not a sharper image.
