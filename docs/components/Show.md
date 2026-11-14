# Show

**Group:** 5 Display  
**Nickname:** Show

Custom preview with line weight or point size and colour, for curves, lines, points, Breps, meshes, clouds and segments.

## Inputs

| Name | Type | Default | Description |
|---|---|---|---|
| `G` | Generic list |  | Geometry: curves, lines, points, Breps, meshes, clouds, segments |
| `col` | Colour |  | Optional: colour; if empty, the point colours of the cloud or black |
| `weight` | Integer | 2 | Line weight or point size in pixels |
| `maxPts` | Integer | 1000000 | Clouds are thinned to this many points in total for display |

## Outputs

| Name | Type | Description |
|---|---|---|
| `Info` | Text | Number of drawn objects and points |

## How it works

Draws any geometry with a chosen line weight or point size and colour, independent of the Grasshopper preview colour. Clouds are thinned to `maxPts` in total for display. With a colour given, point colours of the cloud are ignored.
