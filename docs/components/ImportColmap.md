# ImportColmap

**Group:** 6 Photo  
**Nickname:** Colmap

Reads a COLMAP model exported as text (`cameras.txt`, `images.txt`) and lays the images out flat in the XY plane (the image board), where image points are measured with [Measure](Measure.md).

## Inputs

| Name | Type | Default | Description |
|---|---|---|---|
| `dir` | Text |  | Folder with `cameras.txt` and `images.txt` |
| `img` | Text |  | Folder with the images; default `dir\..\images`, `dir\images` or `dir` |
| `px` | Number | 1 mm | Size of one pixel on the board in document units |
| `cols` | Integer | 6 | Images per row |
| `bake` | Boolean | false | Add the images as locked picture frames on `Megabat::Photos::<name>` |

## Outputs

| Name | Type | Description |
|---|---|---|
| `Photos` | Photo list | Oriented photos: camera plane, intrinsics, rectangle on the board, image file (COLMAP units). The photos draw their image as preview. Parts with [Deconstruct Photo](DeconstructPhoto.md) |
| `Info` | Text | Number of images, missing files, scale, notes |

## Preparing the model in COLMAP

1. Reconstruct the images (or import an existing model).
2. Reconstruction › Dense reconstruction › Select a new folder › **Undistortion**. This gives `images` and `sparse` with a distortion-free PINHOLE camera.
3. File › Import model › that `sparse` folder, then File › Export model as text into the same folder.
4. Optional, dense cloud: in the same dialog **Stereo** and **Fusion**; the resulting `fused.ply` can be read with [ImportCloud](ImportCloud.md).

Cameras with distortion (e.g. SIMPLE_RADIAL) are read, but the distortion is ignored; use the undistorted model.

## The camera plane

Origin = projection centre C = −Rᵀt, X and Y = image axes u (right) and v (down), Z = viewing direction. The rows of the COLMAP rotation R are these axes.
