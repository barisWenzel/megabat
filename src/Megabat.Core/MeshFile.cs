// Mesh files for mesh proxies: read (3dm directly, other formats through Rhino's importers in a headless document),
// write as 3dm, cache like CloudFile. The proxy convention (user strings) is the one of CloudFile.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rhino;
using Rhino.FileIO;
using Rhino.Geometry;

namespace Megabat.Core
{
  public static class MeshFile
  {
    public static readonly string[] Extensions = { ".obj", ".stl", ".ply", ".fbx", ".off", ".3dm" };

    // .ply can hold a cloud or a mesh: mesh if the header declares faces
    public static bool IsMesh(string path)
    {
      var ext = Path.GetExtension(path).ToLowerInvariant();
      if (ext != ".ply") return ext == ".obj" || ext == ".stl" || ext == ".fbx" || ext == ".off";
      try
      {
        using (var r = new StreamReader(path))
          for (int i = 0; i < 200; i++)
          {
            var l = r.ReadLine();
            if (l == null || l.StartsWith("end_header")) break;
            var t = l.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (t.Length == 3 && t[0] == "element" && t[1] == "face" && long.TryParse(t[2], out long n) && n > 0) return true;
          }
      }
      catch { }
      return false;
    }

    class Entry { public DateTime Time; public long Size; public Mesh Mesh; public long Used; }
    static readonly Dictionary<string, Entry> cache = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
    static long tick;
    const int CacheSize = 3;

    // All meshes of the file joined into one; units: 3dm is scaled into the target units, other formats taken as they are
    public static Mesh Load(string path, UnitSystem units = UnitSystem.None)
    {
      var fi = new FileInfo(path);
      if (cache.TryGetValue(path, out var e) && e.Time == fi.LastWriteTimeUtc && e.Size == fi.Length) { e.Used = ++tick; return e.Mesh; }
      var m = Read(path, units);
      cache[path] = new Entry { Time = fi.LastWriteTimeUtc, Size = fi.Length, Mesh = m, Used = ++tick };
      while (cache.Count > CacheSize) cache.Remove(cache.OrderBy(kv => kv.Value.Used).First().Key);
      return m;
    }

    public static Mesh Read(string path, UnitSystem units = UnitSystem.None)
    {
      var r = new Mesh();
      if (Path.GetExtension(path).Equals(".3dm", StringComparison.OrdinalIgnoreCase))
      {
        using (var f = File3dm.Read(path))
        {
          foreach (var o in f.Objects) if (o.Geometry is Mesh m) r.Append(m);
          if (units != UnitSystem.None && f.Settings.ModelUnitSystem != UnitSystem.None)
          {
            double s = RhinoMath.UnitScale(f.Settings.ModelUnitSystem, units);
            if (Math.Abs(s - 1) > 1e-12) r.Transform(Transform.Scale(Point3d.Origin, s));
          }
        }
      }
      else
      {
        using (var doc = RhinoDoc.CreateHeadless(null))
        {
          if (units != UnitSystem.None) doc.ModelUnitSystem = units;
          if (!doc.Import(path)) throw new IOException("Rhino could not import " + Path.GetFileName(path));
          foreach (var o in doc.Objects) if (o.Geometry is Mesh m) r.Append(m);
        }
      }
      if (r.Vertices.Count == 0) throw new IOException("No mesh in " + Path.GetFileName(path));
      return r;
    }

    public static void Write3dm(Mesh m, string path, UnitSystem units)
    {
      using (var f = new File3dm())
      {
        f.Settings.ModelUnitSystem = units;
        f.Objects.AddMesh(m);
        if (!f.Write(path, 8)) throw new IOException("Could not write " + path);
      }
      cache.Remove(path);
    }
  }
}
