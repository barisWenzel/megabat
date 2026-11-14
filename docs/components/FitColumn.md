# FitColumn

**Group:** 3 Elements  
**Nickname:** Column

Round column by circle fit in plan (RANSAC and geometric least squares).

## Inputs

| Name | Type | Default | Description |
|---|---|---|---|
| `C` | Cloud |  | Points of the column, e.g. cut out with CropBrep |
| `tol` | Number | 0.01 | Max. distance of a point to the circle [m] |
| `iter` | Integer | 500 | RANSAC iterations |
| `seed` | Integer | 1 | Random seed |

## Outputs

| Name | Type | Description |
|---|---|---|
| `Circle` | Circle | Circle at the column base |
| `Cylinder` | Brep | Cylinder over the height of the inliers |
| `Inliers` | Cloud | Points on the circle |
| `Info` | Text | Radius, diameter, height, inliers, RMS of the radial deviation, covered circumference (below 90° unreliable), run time |

## How it works

RANSAC on circles through three points in plan, then alternating inlier selection and a geometric least-squares fit (Landau iteration). Coverage tells how much of the circumference is supported by points; below 90° the radius is unreliable.
