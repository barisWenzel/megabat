// CloudFile: read and write point cloud files, and the proxy convention
// A proxy is a thinned point cloud in the Rhino document whose full version lives in a file.
// The object carries the file path in its user strings; Megabat computes with the full file.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rhino.DocObjects;
using Rhino.FileIO;
using Rhino.Geometry;

namespace Megabat.Core
{
  public static class CloudFile
  {
    public const string KeySource = "Megabat.Source";       // absolute path of the full cloud
    public const string KeySourceRel = "Megabat.SourceRel"; // path relative to the .3dm, if possible
    public const string KeyCount = "Megabat.FullCount";     // point count of the full cloud

    public static readonly string[] Extensions = { ".e57", ".ply", ".3dm", ".xyz", ".pts", ".txt", ".csv" };

    // ---- Proxy convention -------------------------------------------------

    // Full cloud file of a proxy object, or null if the object is no proxy or the file is missing
    public static string SourceOf(ObjectAttributes att, string docPath)
    {
      if (att == null) return null;
      var abs = att.GetUserString(KeySource);
      if (!string.IsNullOrEmpty(abs) && File.Exists(abs)) return abs;
      var rel = att.GetUserString(KeySourceRel);
      if (!string.IsNullOrEmpty(rel) && !string.IsNullOrEmpty(docPath))
      {
        var p = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(docPath), rel));
        if (File.Exists(p)) return p;
      }
      return null;
    }

    public static bool IsProxy(ObjectAttributes att) => !string.IsNullOrEmpty(att?.GetUserString(KeySource));

    public static void MarkProxy(ObjectAttributes att, string file, string docPath, int fullCount)
    {
      att.SetUserString(KeySource, Path.GetFullPath(file));
      string rel = null;
      if (!string.IsNullOrEmpty(docPath))
      {
        try { rel = Path.GetRelativePath(Path.GetDirectoryName(docPath), file); } catch { }
      }
      att.SetUserString(KeySourceRel, rel ?? "");
      att.SetUserString(KeyCount, fullCount.ToString(CultureInfo.InvariantCulture));
    }

    public static void Unmark(ObjectAttributes att)
    {
      att.DeleteUserString(KeySource);
      att.DeleteUserString(KeySourceRel);
      att.DeleteUserString(KeyCount);
    }

    // ---- Cache: a file is read once as long as it does not change ---------

    class Entry { public DateTime Time; public long Size; public Rhino.UnitSystem Units; public PointCloud Cloud; public long Used; }
    static readonly Dictionary<string, Entry> cache = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
    static long tick;
    const int CacheSize = 3;

    // units: unit system of the target document. E57 and 3dm are scaled into it;
    // PLY and text files carry no units and are taken as they are.
    public static PointCloud Load(string path, Rhino.UnitSystem units = Rhino.UnitSystem.None)
    {
      path = Path.GetFullPath(path);
      var fi = new FileInfo(path);
      if (!fi.Exists) throw new FileNotFoundException("Point cloud file not found", path);
      lock (cache)
      {
        if (cache.TryGetValue(path, out var e) && e.Time == fi.LastWriteTimeUtc && e.Size == fi.Length && e.Units == units)
        {
          e.Used = ++tick;
          return e.Cloud;
        }
      }
      var c = Read(path, units);
      lock (cache)
      {
        cache[path] = new Entry { Time = fi.LastWriteTimeUtc, Size = fi.Length, Units = units, Cloud = c, Used = ++tick };
        while (cache.Count > CacheSize)
          cache.Remove(cache.OrderBy(kv => kv.Value.Used).First().Key);
      }
      return c;
    }

    public static void Forget(string path)
    {
      lock (cache) cache.Remove(Path.GetFullPath(path));
    }

    // ---- Reading ----------------------------------------------------------

    public static PointCloud Read(string path, Rhino.UnitSystem units = Rhino.UnitSystem.None)
    {
      switch (Path.GetExtension(path).ToLowerInvariant())
      {
        case ".e57": return ReadE57(path, units);
        case ".ply": return ReadPly(path);
        case ".3dm": return Read3dm(path, units);
        case ".xyz": case ".pts": case ".txt": case ".csv": return ReadText(path);
      }
      throw new NotSupportedException("Unsupported file type: " + Path.GetExtension(path));
    }

    // All point clouds of a .3dm file, merged into one and scaled into the target units
    static PointCloud Read3dm(string path, Rhino.UnitSystem units)
    {
      using (var f = File3dm.Read(path))
      {
        if (f == null) throw new InvalidDataException("Cannot read " + path);
        var res = new PointCloud();
        foreach (var o in f.Objects)
          if (o.Geometry is PointCloud pc) res.Merge(pc);
        if (res.Count == 0) throw new InvalidDataException("No point cloud in " + path);
        if (units != Rhino.UnitSystem.None && f.Settings.ModelUnitSystem != Rhino.UnitSystem.None)
        {
          double s = Rhino.RhinoMath.UnitScale(f.Settings.ModelUnitSystem, units);
          if (Math.Abs(s - 1) > 1e-12) res.Transform(Transform.Scale(Point3d.Origin, s));
        }
        return res;
      }
    }

    // E57 through the E57 importer of Rhino, into a headless document (nothing appears in the open file).
    // All scans are merged; the importer applies the scan poses and scales from metres into the target units.
    static PointCloud ReadE57(string path, Rhino.UnitSystem units)
    {
      using (var hd = Rhino.RhinoDoc.CreateHeadless(null))
      {
        hd.ModelUnitSystem = units == Rhino.UnitSystem.None ? Rhino.UnitSystem.Meters : units;
        if (!hd.Import(path)) throw new InvalidDataException("Rhino could not import " + path);
        PointCloud res = null;
        foreach (var o in hd.Objects)
        {
          if (!(o.Geometry is PointCloud pc)) continue;
          if (res == null) res = (PointCloud)pc.Duplicate();
          else res.Merge(pc);
        }
        if (res == null || res.Count == 0) throw new InvalidDataException("No point cloud in " + path);
        return res;
      }
    }

    // Text: one point per line, x y z [i] [r g b] [nx ny nz], separated by space, tab, comma or semicolon.
    // .pts may start with a line holding only the point count.
    static PointCloud ReadText(string path)
    {
      var pts = new List<Point3d>();
      var cols = new List<Color>();
      var nrm = new List<Vector3d>();
      int layout = -1; // number of columns of the first data line
      var sep = new[] { ' ', '\t', ',', ';' };
      using (var r = new StreamReader(path, Encoding.UTF8, true, 1 << 20))
      {
        string line;
        while ((line = r.ReadLine()) != null)
        {
          var f = line.Split(sep, StringSplitOptions.RemoveEmptyEntries);
          if (f.Length < 3) continue; // count line, empty line
          if (!D(f[0], out double x) || !D(f[1], out double y) || !D(f[2], out double z)) continue; // header
          if (layout < 0) layout = f.Length;
          pts.Add(new Point3d(x, y, z));
          // colour columns: x y z r g b (6, 9) or x y z i r g b (7, 10)
          int ci = layout == 6 || layout == 9 ? 3 : layout == 7 || layout == 10 ? 4 : -1;
          if (ci > 0 && f.Length >= ci + 3)
            cols.Add(Color.FromArgb(B(f[ci]), B(f[ci + 1]), B(f[ci + 2])));
          int ni = layout == 9 ? 6 : layout == 10 ? 7 : -1;
          if (ni > 0 && f.Length >= ni + 3 && D(f[ni], out double nx) && D(f[ni + 1], out double ny) && D(f[ni + 2], out double nz))
            nrm.Add(new Vector3d(nx, ny, nz));
        }
      }
      if (pts.Count == 0) throw new InvalidDataException("No points in " + path);
      return CloudUtil.Make(pts.ToArray(), cols.Count == pts.Count ? cols.ToArray() : null, nrm.Count == pts.Count ? nrm.ToArray() : null);
    }

    static bool D(string s, out double v) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    static int B(string s)
    {
      if (!D(s, out double v)) return 0;
      if (v <= 1.0 && s.Contains('.')) v *= 255; // colours as 0..1
      return (int)Math.Max(0, Math.Min(255, Math.Round(v)));
    }

    // PLY: ascii or binary little endian; vertex element with x y z, optional red green blue and nx ny nz
    class Prop { public string Name; public string Type; public int Offset; public int Size; }

    static PointCloud ReadPly(string path)
    {
      using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
      {
        // Header
        var header = new List<string>();
        var sb = new StringBuilder();
        long dataStart = 0;
        while (true)
        {
          int b = fs.ReadByte();
          if (b < 0) throw new InvalidDataException("PLY header incomplete");
          if (b == '\n')
          {
            var l = sb.ToString().TrimEnd('\r').Trim();
            sb.Clear();
            header.Add(l);
            if (l == "end_header") { dataStart = fs.Position; break; }
          }
          else sb.Append((char)b);
          if (header.Count > 1000) throw new InvalidDataException("PLY header too long");
        }
        if (header.Count == 0 || header[0] != "ply") throw new InvalidDataException("Not a PLY file");
        string format = null;
        long nv = -1;
        var props = new List<Prop>();
        bool inVertex = false, vertexFirst = true, seenElement = false;
        foreach (var l in header)
        {
          var t = l.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
          if (t.Length == 0) continue;
          if (t[0] == "format") format = t[1];
          else if (t[0] == "element")
          {
            inVertex = t[1] == "vertex";
            if (inVertex) { nv = long.Parse(t[2], CultureInfo.InvariantCulture); if (seenElement) vertexFirst = false; }
            seenElement = true;
          }
          else if (t[0] == "property" && inVertex)
          {
            if (t[1] == "list") throw new InvalidDataException("PLY vertex lists not supported");
            props.Add(new Prop { Type = t[1], Name = t[2], Size = SizeOf(t[1]) });
          }
        }
        if (nv < 0) throw new InvalidDataException("PLY without vertices");
        if (!vertexFirst) throw new InvalidDataException("PLY: vertex must be the first element");
        if (nv > int.MaxValue) throw new InvalidDataException("PLY too large");
        int n = (int)nv;
        int stride = 0;
        foreach (var p in props) { p.Offset = stride; stride += p.Size; }
        int ix = Idx(props, "x"), iy = Idx(props, "y"), iz = Idx(props, "z");
        if (ix < 0 || iy < 0 || iz < 0) throw new InvalidDataException("PLY without x y z");
        int ir = Idx(props, "red", "r", "diffuse_red"), ig = Idx(props, "green", "g", "diffuse_green"), ib = Idx(props, "blue", "b", "diffuse_blue");
        int inx = Idx(props, "nx", "normal_x"), iny = Idx(props, "ny", "normal_y"), inz = Idx(props, "nz", "normal_z");
        bool hasCol = ir >= 0 && ig >= 0 && ib >= 0, hasNrm = inx >= 0 && iny >= 0 && inz >= 0;

        var pts = new Point3d[n];
        var cols = hasCol ? new Color[n] : null;
        var nrm = hasNrm ? new Vector3d[n] : null;

        if (format == "binary_little_endian")
        {
          // Blocks of 1 M points, parsed in parallel
          const int block = 1 << 20;
          var buf = new byte[(long)Math.Min(n, block) * stride];
          fs.Position = dataStart;
          for (int s = 0; s < n; s += block)
          {
            int len = Math.Min(block, n - s);
            int need = len * stride, got = 0;
            while (got < need)
            {
              int r = fs.Read(buf, got, need - got);
              if (r <= 0) throw new InvalidDataException("PLY data incomplete");
              got += r;
            }
            int s0 = s;
            Parallel.For(0, len, i =>
            {
              int o = i * stride;
              pts[s0 + i] = new Point3d(Val(buf, o, props[ix]), Val(buf, o, props[iy]), Val(buf, o, props[iz]));
              if (hasCol) cols[s0 + i] = Color.FromArgb(C(buf, o, props[ir]), C(buf, o, props[ig]), C(buf, o, props[ib]));
              if (hasNrm) nrm[s0 + i] = new Vector3d(Val(buf, o, props[inx]), Val(buf, o, props[iny]), Val(buf, o, props[inz]));
            });
          }
        }
        else if (format == "ascii")
        {
          fs.Position = dataStart;
          using (var r = new StreamReader(fs, Encoding.ASCII, false, 1 << 20, true))
          {
            var sep = new[] { ' ', '\t' };
            for (int i = 0; i < n; i++)
            {
              var line = r.ReadLine();
              if (line == null) throw new InvalidDataException("PLY data incomplete");
              var f = line.Split(sep, StringSplitOptions.RemoveEmptyEntries);
              double V(int k) => double.Parse(f[k], NumberStyles.Float, CultureInfo.InvariantCulture);
              pts[i] = new Point3d(V(ix), V(iy), V(iz));
              if (hasCol) cols[i] = Color.FromArgb(Cl(V(ir), props[ir]), Cl(V(ig), props[ig]), Cl(V(ib), props[ib]));
              if (hasNrm) nrm[i] = new Vector3d(V(inx), V(iny), V(inz));
            }
          }
        }
        else throw new NotSupportedException("PLY format not supported: " + format);
        return CloudUtil.Make(pts, cols, nrm);
      }
    }

    static int Idx(List<Prop> p, params string[] names)
    {
      for (int i = 0; i < p.Count; i++) if (names.Contains(p[i].Name)) return i;
      return -1;
    }

    static int SizeOf(string t)
    {
      switch (t)
      {
        case "char": case "uchar": case "int8": case "uint8": return 1;
        case "short": case "ushort": case "int16": case "uint16": return 2;
        case "int": case "uint": case "int32": case "uint32": case "float": case "float32": return 4;
        case "double": case "float64": return 8;
      }
      throw new InvalidDataException("PLY type not supported: " + t);
    }

    static double Val(byte[] b, int o, Prop p)
    {
      o += p.Offset;
      switch (p.Type)
      {
        case "char": case "int8": return (sbyte)b[o];
        case "uchar": case "uint8": return b[o];
        case "short": case "int16": return BitConverter.ToInt16(b, o);
        case "ushort": case "uint16": return BitConverter.ToUInt16(b, o);
        case "int": case "int32": return BitConverter.ToInt32(b, o);
        case "uint": case "uint32": return BitConverter.ToUInt32(b, o);
        case "float": case "float32": return BitConverter.ToSingle(b, o);
        default: return BitConverter.ToDouble(b, o);
      }
    }

    static int C(byte[] b, int o, Prop p) => Cl(Val(b, o, p), p);
    // uchar 0..255, ushort 0..65535, float 0..1
    static int Cl(double v, Prop p)
    {
      if (p.Type == "ushort" || p.Type == "uint16") v /= 257.0;
      else if (p.Type.StartsWith("float") || p.Type == "double") v *= 255.0;
      return (int)Math.Max(0, Math.Min(255, Math.Round(v)));
    }

    // ---- Writing: binary PLY with double coordinates (no precision loss for large coordinates)

    public static void WritePly(PointCloud c, string path)
    {
      bool hasCol = c.ContainsColors, hasNrm = c.ContainsNormals;
      int n = c.Count;
      var pts = c.GetPoints();
      var cols = hasCol ? c.GetColors() : null;
      var nrm = hasNrm ? c.GetNormals() : null;
      var tmp = path + ".tmp";
      using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
      {
        var h = new StringBuilder();
        h.Append("ply\nformat binary_little_endian 1.0\ncomment written by Megabat\n");
        h.Append("element vertex ").Append(n).Append('\n');
        h.Append("property double x\nproperty double y\nproperty double z\n");
        if (hasCol) h.Append("property uchar red\nproperty uchar green\nproperty uchar blue\n");
        if (hasNrm) h.Append("property float nx\nproperty float ny\nproperty float nz\n");
        h.Append("end_header\n");
        var hb = Encoding.ASCII.GetBytes(h.ToString());
        fs.Write(hb, 0, hb.Length);
        int stride = 24 + (hasCol ? 3 : 0) + (hasNrm ? 12 : 0);
        const int block = 1 << 18;
        var buf = new byte[(long)Math.Min(Math.Max(n, 1), block) * stride];
        for (int s = 0; s < n; s += block)
        {
          int len = Math.Min(block, n - s), s0 = s;
          Parallel.For(0, len, i =>
          {
            int o = i * stride;
            var p = pts[s0 + i];
            BitConverter.TryWriteBytes(new Span<byte>(buf, o, 8), p.X);
            BitConverter.TryWriteBytes(new Span<byte>(buf, o + 8, 8), p.Y);
            BitConverter.TryWriteBytes(new Span<byte>(buf, o + 16, 8), p.Z);
            o += 24;
            if (hasCol) { var col = cols[s0 + i]; buf[o] = col.R; buf[o + 1] = col.G; buf[o + 2] = col.B; o += 3; }
            if (hasNrm)
            {
              var v = nrm[s0 + i];
              BitConverter.TryWriteBytes(new Span<byte>(buf, o, 4), (float)v.X);
              BitConverter.TryWriteBytes(new Span<byte>(buf, o + 4, 4), (float)v.Y);
              BitConverter.TryWriteBytes(new Span<byte>(buf, o + 8, 4), (float)v.Z);
            }
          });
          fs.Write(buf, 0, len * stride);
        }
      }
      if (File.Exists(path)) File.Delete(path);
      File.Move(tmp, path);
      Forget(path);
    }
  }
}
