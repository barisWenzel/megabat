# Examples

One Grasshopper file per workflow. Each file starts with a yellow note (what it shows, which data it needs); the steps are numbered, coloured groups from left to right.

Open a Rhino file in **metres** for 01 to 04 and 06 (the point cloud is in metres). For 05 open `05_Photo.3dm`, which is in millimetres. The paths in the files are relative (`data\S170_1cm.ply`): they work as long as the .gh files stay in this folder.

| File | Shows | Data |
|---|---|---|
| `01_Cloud.gh` | ImportCloud, Downsample, CropBrep, SplitSlices, MergeClouds, Show, ExportCloud | `data/S170_1cm.ply` |
| `02_Segments.gh` | Segmentize, Classify, SegmentRect with outline, Deconstruct Segment | `data/S170_1cm.ply` |
| `03_Snap.gh` | SnapPolygon (room from a rough sketch), SnapOpening (door with reveal depth) | `data/S170_1cm.ply` |
| `04_Section.gh` | Section as plan and as vertical section, view beyond with Show | `data/S170_1cm.ply` |
| `05_Photo.gh` + `05_Photo.3dm` | ImportColmap, Measure, Intersect, Georef, Ortho | South Building (download, see below) |
| `06_Detect.gh` | ViewPoints and Detect (experimental script): objects from a text prompt with Grounding DINO | `data/S170_1cm.ply`, Python setup in [scripts/README.md](../scripts/README.md) |

## 01 Cloud

1. ImportCloud reads `data\S170_1cm.ply` (918 476 points).
2. Downsample thins it to a 5 cm voxel grid.
3. CropBrep keeps the points inside the stored box; `Rest` holds the others.
4. SplitSlices cuts at two horizontal planes, Show draws the three slices, MergeClouds joins them again.
5. ExportCloud writes the thinned cloud next to the .gh file when you press `write`.

## 02 Segments

1. Import and thin to 3 cm.
2. Segmentize finds 15 planes, Classify sorts them into horizontal, vertical and inclined and gives the main direction.
3. SegmentRect makes one rectangle per segment, trimmed to the neighbours. With `cell` > 0, `Shape` follows the outline of the points, with openings as holes.
4. The slider `i` picks one segment for Deconstruct Segment.

## 03 Snap

The file stores two rough sketches: a room outline drawn 15 to 25 cm off the walls, and a rectangle around a door.

1. Import, thin, Segmentize, Classify.
2. SnapPolygon moves every edge of the room sketch onto its wall; `Room` is the solid between floor and ceiling.
3. SnapOpening finds the door in the full cloud: 0.91 × 2.04 m, with reveal depth.

To use your own sketches, draw curves in Rhino and reference them in the curve parameters (right click › Set one Curve).

## 04 Section

1. A horizontal plane 1.2 m above the floor, looking down 1.3 m: plan with cut lines and the view beyond.
2. A vertical plane, looking 5 m along +Y: section.
3. Show draws the view beyond lighter than the cut.

## 05 Photo

Data: [South Building](https://demuc.de/colmap/datasets/) from the COLMAP datasets (128 photos, not included here).

1. Download `south-building.zip` and unpack it.
2. In COLMAP: File › Import model › `south-building\sparse`, then Reconstruction › Dense reconstruction › Select a new folder (e.g. `south-building\dense`) › **Undistortion**.
3. File › Import model › `dense\sparse`, then File › Export model as text into the same folder.
4. Open `05_Photo.3dm` in Rhino, then `05_Photo.gh`, and set `dir` to the `dense\sparse` folder.

The .3dm holds the measured points P003 to P010 on the layer `Megabat::Measure`; P007 to P010 are the corners of a window. Intersect turns them into 3D points, Georef scales them with the window width (1.2 m) and lays the window flat into XY, Ortho rectifies it (top view). With `Add` you measure new points.

## 06 Detect

Needs Python with PyTorch and an NVIDIA GPU; setup in [scripts/README.md](../scripts/README.md). ViewPoints chooses camera standpoints that together see most of the room; Detect renders 8 views from each. Set the prompt (classes separated by a dot and a space), then switch `run` to true. One run takes about a minute.

## Data and licence

`data/S170_1cm.ply` is room S170 from the test area of the 3DSES dataset, thinned to 1 cm voxels:

Mérizette, M., Audebert, N., Kervella, P., Verdun, J. (2024). *3DSES: an indoor Lidar point cloud segmentation dataset with real and pseudo-labels from a 3D model.* Zenodo. [doi:10.5281/zenodo.13323342](https://doi.org/10.5281/zenodo.13323342). Licence: [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/).

The thinned file is shared under the same licence. The code of Megabat is MIT licensed (see [LICENSE](../LICENSE)).
