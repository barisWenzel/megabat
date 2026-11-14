# Segmentize

**Group:** 2 Segments  
**Nickname:** Segmentize

Find planes in the cloud (sequential RANSAC). Each plane becomes a segment with plane, points and type.

## Inputs

| Name | Type | Default | Description |
|---|---|---|---|
| `C` | Cloud |  | Point cloud, best thinned with Downsample first (a few 100 000 points) |
| `tol` | Number | 0.015 | Max. distance of a point to the plane [m] |
| `minPts` | Integer | 60 | Minimum number of points per segment |
| `maxPlanes` | Integer | 15 | Maximum number of planes to find |
| `iter` | Integer | 1000 | Plane hypotheses per pass |
| `sample` | Integer | 1000 | Sample size used to score the hypotheses |
| `seed` | Integer | 3 | Random seed (same value = same result) |

## Outputs

| Name | Type | Description |
|---|---|---|
| `Segments` | Segment list | Segments in order of detection, coloured; type preliminary with 10° tolerance |
| `Rest` | Cloud | Unassigned points (preview off by default) |
| `Info` | Text | Number of segments, rest, run time |

## How it works

Sequential RANSAC: three random points define a plane hypothesis; points closer than `tol` count as inliers. The hypothesis with most inliers (scored on a sample) wins, is refined by least squares and removed from the cloud. This repeats until `maxPlanes` is reached or fewer than `minPts` points would remain. Large surfaces are found first.
Triples are drawn beforehand from `seed`, so the result is reproducible despite multithreading. The `Rest` output is hidden in the preview by default.
