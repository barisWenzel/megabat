# ViewPoints

**Group:** 1 Cloud  
**Nickname:** Views

Standpoints that see as much of the surfaces as possible: camera positions for [Detect](../../scripts/README.md) or scanner positions for planning a scan.

## Inputs

| Name | Type | Default | Description |
|---|---|---|---|
| `C` | Cloud |  | Point cloud |
| `voxel` | Number | 0.1 | Voxel size of the occupancy grid [m] |
| `height` | Number | 1.5 | Height of the standpoints above the floor [m] |
| `step` | Number | 0.5 | Spacing of the candidate grid [m] |
| `clear` | Number | 0.3 | Free space around a standpoint [m] |
| `dMin` | Number | 0.5 | Minimum distance (blind spot) [m] |
| `dMax` | Number | 15 | Maximum distance [m] |
| `angle` | Number | 70 | Maximum incidence angle to the surface normal [°] |
| `cover` | Number | 0.95 | Stop when this share of the surface voxels is seen |
| `max` | Integer | 5 | Maximum number of standpoints |
| `room` | Boolean | true | Only candidates below a ceiling (indoors); false = also outdoors |

## Outputs

| Name | Type | Description |
|---|---|---|
| `Pts` | Point list | Standpoints in the order chosen |
| `Cover` | Number list | Cumulative share of the surface voxels seen after each standpoint |
| `Seen` | Cloud list | Voxels first seen from each standpoint, to check the result |
| `Cand` | Point list | All candidate positions (preview off) |
| `Info` | Text | Voxels, candidates, standpoints, coverage, run time |

## How it works

1. **Voxels.** The cloud is sorted into an occupancy grid. Every voxel with at least 3 points counts as surface and gets a normal from the covariance of its points.
2. **Candidates.** On a grid with spacing `step`, the floor is the lowest occupied voxel of each column; the candidate lies `height` above it, needs `clear` free space around it and, with `room`, a ceiling above it.
3. **Visibility.** From each candidate a ray runs to every surface voxel through the grid (3D DDA). The voxel is seen if no other voxel blocks the ray, its distance lies between `dMin` and `dMax` and the ray meets the surface at no more than `angle` from its normal.
4. **Choice.** Greedy set cover: the next standpoint is the one that adds the most voxels not seen yet, until `cover` or `max` is reached.

The share in `Cover` refers to all surface voxels, including those no candidate can see (behind furniture, inside walls); 100 % is therefore rarely reached. For the test room S170, five standpoints see 92 % in 0.2 s.
