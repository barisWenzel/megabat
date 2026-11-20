using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Rhino.Display;
using Rhino.Geometry;
using Megabat.Core;

namespace Megabat.Display
{
  // Draws a point cloud with level of detail: visible octree nodes, largest on screen first, until the point budget is used.
  // The octree is built once per cloud in the background; until then the thinned cloud is shown.
  public static class LodDraw
  {
    class State { public LodNode Root; public Task Build; }
    static readonly ConditionalWeakTable<PointCloud, State> Cache = new ConditionalWeakTable<PointCloud, State>();
    const double RefinePx = 150;   // children are only drawn if the parent covers more than this on screen

    // false = octree not ready yet (caller draws the thinned cloud)
    public static bool TryDraw(PointCloud c, DisplayPipeline dp, Color color, int budget)
    {
      var st = Cache.GetValue(c, _ => new State());
      if (st.Root == null)
      {
        if (st.Build == null)
          st.Build = Task.Run(() =>
          {
            st.Root = LodCore.Build(c);
            Rhino.RhinoApp.InvokeOnUiThread(new Action(() => Rhino.RhinoDoc.ActiveDoc?.Views.Redraw()));
          });
        return false;
      }
      var vp = dp.Viewport;
      var queue = new PriorityQueue<LodNode, double>();
      void Push(LodNode n)
      {
        if (n?.Cloud == null || n.Cloud.Count == 0 || !vp.IsVisible(n.Box)) return;
        queue.Enqueue(n, -Size(vp, n));
      }
      Push(st.Root);
      int drawn = 0;
      while (queue.Count > 0 && drawn < budget)
      {
        var n = queue.Dequeue();
        dp.DrawPointCloud(n.Cloud, 2, color);
        drawn += n.Cloud.Count;
        if (n.Kids != null && Size(vp, n) > RefinePx)
          foreach (var k in n.Kids) Push(k);
      }
      return true;
    }

    // projected size of the node box in pixels
    static double Size(RhinoViewport vp, LodNode n)
    {
      var c = n.Box.Center;
      if (!vp.GetWorldToScreenScale(c, out double ppu)) return double.MaxValue;
      return n.Box.Diagonal.Length * ppu;
    }
  }
}
