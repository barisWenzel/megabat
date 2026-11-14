# Downsample

**Group:** 1 Cloud  
**Nickname:** Down

Thin out a cloud: every k-th point (fast) or a voxel grid (one averaged point per cell), optionally to a target point count.

## Inputs

| Name | Type | Default | Description |
|---|---|---|---|
| `C` | Cloud |  | Point cloud (any size, read in blocks) |
| `voxel` | Number | 0 | Optional: voxel edge length [m]; if > 0 it overrides N |
| `N` | Integer | 500000 | Target point count (only if voxel is 0) |
| `uniform` | Boolean | false | false = every k-th point, exactly N (fast, density stays uneven); true = voxel size is searched so that about N points remain (spatially even, ±5 %, slower) |

## Outputs

| Name | Type | Description |
|---|---|---|
| `Cloud` | Cloud | Thinned cloud (voxel: mean per cell, colour and normal averaged) |
| `Info` | Text | Point count before and after, voxel size or step, run time |

## How it works

With `voxel = 0` and `uniform = false` (default) every k-th point is kept until exactly `N` points remain. This is the fastest option, but dense areas close to the scanner stay dense.
With `uniform = true` the voxel size is searched (secant steps on a log scale) so that about `N` occupied cells remain; each cell becomes one point with averaged position, colour and normal. A given `voxel > 0` is used directly.
The cloud is read in blocks in parallel, so clouds with tens of millions of points work.
