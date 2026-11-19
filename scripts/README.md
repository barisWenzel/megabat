# Experimental scripts

C# script components for Grasshopper in Rhino 8, tested here before they move into the plugin. To use one, place a C# Script component, open it (double click), paste the file and set the inputs and outputs as listed in the file header. Changes take effect without restarting Rhino. To reuse a component in other files: select it › File › Create User Object.

All scripts are experimental: inputs, outputs and results may change.

## Segments

### RectOutline.cs

Intersects the rectangle of a segment (from SegmentRect) with the raster outline of its points, for L-shaped faces and openings.

| Input | Description |
|---|---|
| `R` | Rectangle from SegmentRect › Rect |
| `P` | Points of the segment (Deconstruct Segment › Points) |
| `cell` | Raster size [m], default 0.05 |
| `close` | Cells for closing gaps, default 2 |
| `edge` | Max. gap between points and rectangle edge that is filled [m], default 0.3 |

Outputs: `Outline` (outer outline and openings), `Srf` (planar surfaces, openings as holes), `Info`.

## Object detection

### Detect.cs + detect.py

Open-vocabulary object detection in a point cloud with Grounding DINO: the cloud is rendered into views, Grounding DINO finds the objects named in a text prompt, and the 2D boxes are mapped back to the points.

**Setup** (once, needs an NVIDIA GPU):

1. Install Python 3.12 or newer.
2. In a command prompt:
   ```
   mkdir C:\megabat_detect
   cd C:\megabat_detect
   py -m venv venv
   venv\Scripts\activate
   pip install torch torchvision --index-url https://download.pytorch.org/whl/cu128
   pip install transformers pillow
   ```
   Use the CUDA version that matches your driver (pytorch.org › Get Started).
3. Copy `detect.py` to `C:\megabat_detect`. The model (`IDEA-Research/grounding-dino-base`, about 1 GB) is downloaded on the first run.

| Input | Description |
|---|---|
| `C` | Point cloud (Megabat Cloud or Rhino point cloud) |
| `prompt` | Classes separated by a dot and a space, in English, e.g. `radiator. door. window.` |
| `cam` | Camera standpoints (points, `views` horizontal views each) and/or names of Rhino named views; `active` = current view; empty = centre of the cloud |
| `views` | Views per standpoint, default 8 |
| `score` | Minimum detection score, default 0.35 |
| `depth` | Max. depth of an object behind its front [m], default 0.5 |
| `run` | true = run (about 30 to 60 s, the model is loaded each time) |

Outputs: `Clouds` (points per object), `Label`, `Score`, `Box` (3D bounding box), `Info`. The rendered views are written to `C:\megabat_detect\views`.

Known limits: grey (unscanned) areas are sometimes detected as doors; dense clouds give much better views than thinned ones.

## Photogrammetry from COLMAP

Measure points in oriented photos and intersect them in 3D. COLMAP itself runs outside Rhino.

**In COLMAP:**

1. Reconstruct the images (or import an existing model).
2. Reconstruction › Dense reconstruction › Select a new folder › **Undistortion**. This gives `images` and `sparse` with a distortion-free PINHOLE camera.
3. File › Import model › that `sparse` folder, then File › Export model as text into the same folder.
4. Optional, dense cloud: in the same dialog **Stereo** and **Fusion**; the resulting `fused.ply` can be read with ImportCloud.

### ImportColmap.cs

Reads `cameras.txt` and `images.txt` and lays the images out flat in the XY plane (1 px = 1 mm by default) as preview.

| Input | Description |
|---|---|
| `dir` | Folder with cameras.txt and images.txt |
| `img` | Folder with the images (optional; default `dir\..\images`, `dir\images` or `dir`) |
| `px` | Size of one pixel in document units, default 1 mm |
| `cols` | Images per row, default 6 |
| `bake` | true = add the images as locked picture frames on `Megabat::Photos::<name>` |

Outputs: `Cam` (camera planes: origin = projection centre, X/Y = image axes, Z = viewing direction), `K` (fx fy cx cy width height), `Frame` (image rectangles), `Name`, `Info`.

### Measure.cs

Buttons `Add`, `Undo`, `Clear`. Add minimises Grasshopper; click the same feature in 2 or 3 images, Enter. The clicks become Rhino points on `Megabat::Measure`, named P001, P002 ..., one colour per point. They can be moved, copied and deleted in Rhino; points with the same name belong together.

With `Cam`, `K` and `Frame` from ImportColmap connected:

1. after the first click the epipolar line is drawn in all other images, images that cannot see the point are greyed out,
2. the next click snaps onto that line (option `Snap` in the command line),
3. after the second click a crosshair shows the predicted position in the remaining images, and the command line shows the residual in pixels.

Outputs: `P` (tree, one branch per point), `Name`, `Info`.

### Intersect.cs

Forward intersection of the clicks.

| Input | Description |
|---|---|
| `P` | Tree from Measure |
| `Cam`, `K`, `Frame` | From ImportColmap |

Outputs: `Pt` (3D point per branch), `Rays` (camera to point), `Res` (distance of each ray to the point), `Angle` (largest intersection angle [°]), `Info`.

Results are in COLMAP units, i.e. without scale. A scale from a known distance is planned.

