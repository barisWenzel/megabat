# Experimental scripts

C# script components for Grasshopper in Rhino 8, tested here before they move into the plugin. To use one, place a C# Script component, open it (double click), paste the file and set the inputs and outputs as listed in the file header. Changes take effect without restarting Rhino. To reuse a component in other files: select it › File › Create User Object.

All scripts are experimental: inputs, outputs and results may change.

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

## Moved into the plugin

The earlier scripts RectOutline, ImportColmap, Measure and Intersect are now plugin components: the outline is an option of [SegmentRect](../docs/components/SegmentRect.md), the photo tools are in the group 6 Photo ([ImportColmap](../docs/components/ImportColmap.md), [Measure](../docs/components/Measure.md), [Intersect](../docs/components/Intersect.md), [Georef](../docs/components/Georef.md), [Ortho](../docs/components/Ortho.md)). The script files are kept here for reference.
