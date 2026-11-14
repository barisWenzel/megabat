using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using Grasshopper.Kernel;
using Megabat.Types;
using Megabat.Core;

namespace Megabat.Components
{
  // Base of all Megabat components: Message = name, timing, icon from abbreviation and group colour
  public abstract class MegabatComponent : GH_Component
  {
    protected Stopwatch Watch;

    protected MegabatComponent(string name, string nick, string description, string sub)
      : base(name, nick, description, Tab.Name, sub)
    {
      Message = name;
    }

    protected abstract string IconText { get; }
    protected abstract Color IconColor { get; }
    protected override Bitmap Icon => Icons.For(IconText, IconColor);

    protected override void BeforeSolveInstance()
    {
      Message = Name;
      Watch = Stopwatch.StartNew();
      base.BeforeSolveInstance();
    }

    protected string Ms => Watch == null ? "" : Watch.Elapsed.TotalMilliseconds.ToString("0") + " ms";

    // Parameter helpers
    protected static int AddCloud(GH_InputParamManager p, string name, string desc, GH_ParamAccess access = GH_ParamAccess.item, bool optional = false)
    {
      int i = p.AddParameter(new CloudParam(), name, name, desc, access);
      p[i].Optional = optional;
      return i;
    }
    protected static int AddCloud(GH_OutputParamManager p, string name, string desc, GH_ParamAccess access = GH_ParamAccess.item)
      => p.AddParameter(new CloudParam(), name, name, desc, access);

    protected static int AddSegments(GH_InputParamManager p, string name, string desc, bool optional = false)
    {
      int i = p.AddParameter(new SegmentParam(), name, name, desc, GH_ParamAccess.list);
      p[i].Optional = optional;
      return i;
    }
    protected static int AddSegments(GH_OutputParamManager p, string name, string desc)
      => p.AddParameter(new SegmentParam(), name, name, desc, GH_ParamAccess.list);

    protected static void Optional(GH_InputParamManager p, params int[] idx) { foreach (var i in idx) p[i].Optional = true; }

    // Reading helpers
    protected static List<Segment> Segs(List<SegmentGoo> g)
    {
      var r = new List<Segment>();
      if (g != null) foreach (var s in g) if (s?.Value != null) r.Add(s.Value);
      return r;
    }
    protected static List<SegmentGoo> Wrap(IEnumerable<Segment> s)
    {
      var r = new List<SegmentGoo>();
      foreach (var x in s) r.Add(new SegmentGoo(x));
      return r;
    }
  }
}
