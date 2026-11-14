# SplitSlices

**Group:** 1 Cloud  
**Nickname:** Split

Split a cloud into slices at one or more parallel planes, in blocks for very large clouds.

## Inputs

| Name | Type | Default | Description |
|---|---|---|---|
| `C` | Cloud |  | Point cloud |
| `P` | Plane list |  | Cutting planes, parallel; order does not matter, they are sorted along the normal of the first plane |
| `overlap` | Number | 0 | Points closer than overlap to a plane go into both neighbouring slices [m] |

## Outputs

| Name | Type | Description |
|---|---|---|
| `Clouds` | Cloud list | Slices from bottom to top (count = planes + 1) |
| `Info` | Text | Points per slice, run time |

## How it works

The planes are sorted along the normal of the first plane; each point is assigned by binary search to its slice. `overlap` copies points near a plane into both neighbouring slices, which helps when walls should be found per storey.
