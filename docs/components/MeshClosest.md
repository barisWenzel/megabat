# MeshClosest

**Group:** 1 Cloud  
**Nickname:** MClosest

Closest points on a mesh for many points or a whole cloud, on all processor cores, e.g. to compare a scan with a mesh.

## Inputs

| Name | Type | Default | Description |
|---|---|---|---|
| `M` | Mesh |  | Mesh; a mesh proxy is read in full from its file |
| `P` | Point list |  | Optional: query points |
| `C` | Cloud |  | Optional: query cloud |
| `max` | Number | 0 | Search distance; points farther away get no result (distance -1). 0 = unlimited |

## Outputs

| Name | Type | Description |
|---|---|---|
| `Points` | Point list | Closest points on the mesh (P first, then C) |
| `Distance` | Number list | Distances (-1 = nothing within max) |
| `Face` | Integer list | Face index of each closest point (-1 = none) |
| `Info` | Text | Point count, mean and maximum distance, run time |

## How it works

The search tree of the mesh is built once, then all query points run in parallel. One million points on a mesh with 2 million faces take about 11 s, about five times faster than Mesh Closest Point. A search distance `max` makes it faster still.
