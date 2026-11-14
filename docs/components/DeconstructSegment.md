# Deconstruct Segment

**Group:** 2 Segments  
**Nickname:** DeSeg

Split a segment into plane, points, type and number.

## Inputs

| Name | Type | Default | Description |
|---|---|---|---|
| `Seg` | Segment |  | Segment |

## Outputs

| Name | Type | Description |
|---|---|---|
| `Plane` | Plane | Plane of the segment |
| `Points` | Cloud | Points of the segment |
| `Type` | Integer | -1 = unknown, 0 = horizontal, 1 = vertical, 2 = inclined |
| `Id` | Integer | Number from Segmentize |

## How it works

Unpacks a segment into its plane, its points (as Megabat cloud), its type (-1 unknown, 0 horizontal, 1 vertical, 2 inclined) and the number from Segmentize.
