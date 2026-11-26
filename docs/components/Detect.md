# Detect (experimental)

**Group:** 3 Elements  
**Nickname:** Detect

Find objects named in a text prompt, e.g. `radiator. table.`, with the open-vocabulary detector Grounding DINO. The cloud is rendered into views, the 2D boxes are mapped back to the points.

Needs Python with PyTorch and an NVIDIA GPU in a separate folder; setup in [scripts/README.md](../../scripts/README.md). Everything else in Megabat works without it.

## Inputs

| Name | Type | Default | Description |
|---|---|---|---|
| `C` | Cloud |  | Point cloud, with colours if possible; clouds thinned to more than about 2 cm give poor views |
| `prompt` | Text |  | Classes in English, separated by a dot and a space |
| `cam` | Generic list |  | Optional: standpoints (points, e.g. from [ViewPoints](ViewPoints.md)) and/or names of Rhino named views, `active` = current view; empty = centre of the cloud |
| `views` | Integer | 8 | Horizontal views per standpoint |
| `score` | Number | 0.45 | Minimum detection score (0 to 1) |
| `depth` | Number | 0.5 | Max. depth of an object behind its front [m] |
| `minViews` | Integer | 2 | An object must be detected in at least this many views; false detections rarely repeat |
| `run` | Boolean | false | true = run (about 30 to 60 s); results are kept when set back to false |
| `folder` | Text | `C:\megabat_detect` | Folder with `venv\Scripts\python.exe` and `detect.py` |

## Outputs

| Name | Type | Description |
|---|---|---|
| `Clouds` | Cloud list | Points of each object |
| `Label` | Text list | Class of each object |
| `Score` | Number list | Best detection score of each object |
| `Box` | Box list | 3D bounding box of each object |
| `Info` | Text | Objects per class, detections per view, dropped objects, run time |

## How it works

1. **Views.** From each standpoint, `views` horizontal views with 90° field of view are rendered as images (splats with a depth buffer).
2. **Detection.** `detect.py` runs Grounding DINO on all images in a separate Python process.
3. **Back to the points.** Each 2D box takes the front-most points of its pixels; points more than `depth` behind the front are dropped.
4. **Merge.** Detections of the same class are merged when they share 10 cm cells or their boxes overlap.
5. **Confirmation.** Objects seen in fewer than `minViews` views or with fewer than 300 points are dropped.

Walls and floors are better found with [Segmentize](Segmentize.md). Known limits: grey (unscanned) areas are sometimes detected as doors; boxes of large objects may include part of the wall behind.
