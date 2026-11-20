# Deconstruct Photo

**Group:** 6 Photo  
**Nickname:** DePhoto

Splits a photo from [ImportColmap](ImportColmap.md) into its parts.

## Inputs

| Name | Type | Description |
|---|---|---|
| `Photo` | Photo | Photo |

## Outputs

| Name | Type | Description |
|---|---|---|
| `Cam` | Plane | Camera plane: origin = projection centre, X/Y = image axes, Z = viewing direction |
| `Frame` | Rectangle | Rectangle of the image on the board |
| `Name` | Text | Image file name |
| `Path` | Text | Full path of the image file (empty if not found) |
| `fx`, `fy` | Number | Focal length [px] |
| `cx`, `cy` | Number | Principal point [px] |
| `W`, `H` | Integer | Image size [px] |
