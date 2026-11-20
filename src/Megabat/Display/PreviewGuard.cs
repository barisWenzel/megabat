using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using HarmonyLib;
using Rhino.Geometry;
using Megabat.Core;

namespace Megabat.Display
{
  // Thins the preview of all Grasshopper point clouds (Harmony patch on GH_PointCloud).
  // Switch in the canvas toolbar next to the preview modes. Always on at start; the point limit is stored in the GH settings.
  public static class PreviewGuard
  {
    const string Id = "megabat.previewguard";
    const string KeyMax = "Megabat.PreviewGuard.MaxPts";
    static readonly int[] Steps = { 250000, 500000, 1000000, 2000000 };
    public const int DefaultMax = 1000000;

    // Switching off only lasts for the current session
    public static bool Enabled { get; set; } = true;
    public static int MaxPts
    {
      get => Instances.Settings.GetValue(KeyMax, DefaultMax);
      set { Instances.Settings.SetValue(KeyMax, value); Instances.Settings.WritePersistentSettings(); }
    }
    // Level of detail (octree) instead of a fixed thinned cloud; MaxPts is then the point budget per cloud and view
    const string KeyLod = "Megabat.PreviewGuard.Lod";
    public static bool Lod
    {
      get => Instances.Settings.GetValue(KeyLod, false);
      set { Instances.Settings.SetValue(KeyLod, value); Instances.Settings.WritePersistentSettings(); }
    }
    public static void SetLod(bool on) { Lod = on; Refresh(); }
    public static bool Patched { get; private set; }
    public static string Error { get; private set; }
    public static event Action Changed;

    static readonly ConditionalWeakTable<PointCloud, Thin> cache = new ConditionalWeakTable<PointCloud, Thin>();
    static AccessTools.FieldRef<GH_Goo<PointCloud>, PointCloud> valueRef;
    class Thin { public PointCloud Cloud; public int Max; }
    static ToolStripSplitButton btn;
    static ToolStripTextBox box;
    static bool hinted;

    // Hint once per session, as soon as a cloud is actually thinned
    public static void Notify()
    {
      if (hinted) return;
      hinted = true;
      Rhino.RhinoApp.InvokeOnUiThread(new Action(() =>
        MessageBox.Show(
          "Large point clouds are thinned to at most " + MaxPts.ToString("N0") + " points in the preview " +
          "so that Rhino stays responsive. Computation always uses all points.\n\n" +
          "Switch on and off: PreviewGuard button in the Grasshopper toolbar next to the preview modes " +
          "(arrow: choose the point limit or type your own). It is switched on again at every start.",
          "Megabat PreviewGuard", MessageBoxButtons.OK, MessageBoxIcon.Information)));
    }

    public static void Start()
    {
      Ensure();
      try { EnsureButton(); } catch { }
    }

    public static void Ensure()
    {
      if (Patched) return;
      try
      {
        valueRef = AccessTools.FieldRefAccess<GH_Goo<PointCloud>, PointCloud>("m_value");
        var h = new Harmony(Id);
        var target = AccessTools.Method(typeof(GH_PointCloud), "DrawViewportWires", new[] { typeof(GH_PreviewWireArgs) });
        h.Patch(target,
          prefix: new HarmonyMethod(typeof(PreviewGuard), nameof(Prefix)),
          finalizer: new HarmonyMethod(typeof(PreviewGuard), nameof(Finalizer)));
        Patched = true; Error = null;
      }
      catch (Exception e) { Patched = false; Error = "Patch failed: " + e.Message; }
    }

    public static void Set(bool on, int max)
    {
      Enabled = on; MaxPts = max;
      Refresh();
    }

    static void Refresh()
    {
      UpdateButton();
      Changed?.Invoke();
      Instances.RedrawCanvas();
      Rhino.RhinoDoc.ActiveDoc?.Views.Redraw();
    }

    // Before drawing: temporarily replace the value with the thinned cloud
    static void Prefix(GH_PointCloud __instance, out PointCloud __state)
    {
      __state = null;
      if (!Enabled) return;
      var full = valueRef(__instance);
      int max = MaxPts;
      if (full == null || full.Count <= max) return;
      Notify();
      if (!cache.TryGetValue(full, out Thin t) || t.Max != max)
      {
        if (t != null) cache.Remove(full);
        t = new Thin { Cloud = CloudUtil.Thin(full, max), Max = max };
        cache.Add(full, t);
      }
      __state = full;
      valueRef(__instance) = t.Cloud;
    }

    // Always restore afterwards, also on exceptions
    static Exception Finalizer(GH_PointCloud __instance, PointCloud __state, Exception __exception)
    {
      if (__state != null) valueRef(__instance) = __state;
      return __exception;
    }

    // ---- Switch in the canvas toolbar ----
    static void EnsureButton()
    {
      var ed = Instances.DocumentEditor;
      if (ed == null) return;
      var ts = FindStrip(ed, "CanvasToolbar");
      if (ts == null) return;
      if (btn != null && ts.Items.Contains(btn)) { UpdateButton(); return; }
      foreach (var n in new[] { "PreviewGuardButton", "PreviewGuardSep" })
      {
        var old = ts.Items[n];
        if (old != null) { ts.Items.Remove(old); old.Dispose(); }
      }
      var b = new ToolStripSplitButton
      {
        Name = "PreviewGuardButton",
        DisplayStyle = ToolStripItemDisplayStyle.Image,
        ImageScaling = ToolStripItemImageScaling.None
      };
      b.ButtonClick += (s, e) => Set(!Enabled, MaxPts);
      foreach (var m in Steps)
      {
        int mm = m;
        var it = new ToolStripMenuItem("max " + (m >= 1000000 ? (m / 1000000.0) + " M" : (m / 1000) + " k") + " points per cloud") { Tag = m };
        it.Click += (s, e) => Set(true, mm);
        b.DropDownItems.Add(it);
      }
      b.DropDownItems.Add(new ToolStripSeparator());
      b.DropDownItems.Add(new ToolStripLabel("Custom value (Enter):"));
      box = new ToolStripTextBox { ToolTipText = "Maximum points per cloud, e.g. 750000" };
      box.KeyDown += (s, e) =>
      {
        if (e.KeyCode != Keys.Enter) return;
        e.SuppressKeyPress = true;
        var t = box.Text.Replace(".", "").Replace(",", "").Replace(" ", "").Replace("'", "");
        if (int.TryParse(t, out int v) && v > 0) { Set(true, v); b.HideDropDown(); }
        else System.Media.SystemSounds.Beep.Play();
      };
      b.DropDownItems.Add(box);
      b.DropDownItems.Add(new ToolStripSeparator());
      var lod = new ToolStripMenuItem("Level of detail (octree): full detail when zooming in") { Name = "PreviewGuardLod",
        ToolTipText = "Megabat clouds are drawn from an octree: only visible parts, finer the closer they are, within the point limit. Built once per cloud in the background. Per cloud: right click on the Cloud parameter › Display." };
      lod.Click += (s, e) => SetLod(!Lod);
      b.DropDownItems.Add(lod);
      var sep = new ToolStripSeparator { Name = "PreviewGuardSep" };
      var off = ts.Items["PreviewOffButton"];
      int idx = off != null ? ts.Items.IndexOf(off) + 1 : ts.Items.Count;
      ts.Items.Insert(idx, sep);
      ts.Items.Insert(idx + 1, b);
      btn = b;
      UpdateButton();
    }

    static ToolStrip FindStrip(Control c, string name)
    {
      if (c is ToolStrip t && t.Name == name) return t;
      foreach (Control ch in c.Controls) { var r = FindStrip(ch, name); if (r != null) return r; }
      return null;
    }

    static void UpdateButton()
    {
      if (btn == null) return;
      bool on = Enabled; int max = MaxPts;
      var old = btn.Image;
      btn.Image = Icon(on);
      old?.Dispose();
      btn.ToolTipText = "PreviewGuard (Megabat) " + (on ? "on" : "off") + ": large point clouds are thinned to at most " +
        max.ToString("N0") + " points in the preview.\nClick: on/off, arrow: choose point limit";
      foreach (ToolStripItem it in btn.DropDownItems)
        if (it is ToolStripMenuItem mi) mi.Checked = mi.Tag is int t ? on && t == max : mi.Name == "PreviewGuardLod" && Lod;
      if (box != null) box.Text = max.ToString("N0");
    }

    // Icon: on = few blue points with green tick, off = dense grey grid
    static Bitmap Icon(bool on)
    {
      var bmp = new Bitmap(24, 24);
      using (var g = Graphics.FromImage(bmp))
      {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        if (on)
        {
          using (var br = new SolidBrush(Color.FromArgb(40, 90, 200)))
            for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) if ((i + j) % 2 == 0) g.FillEllipse(br, 2 + i * 5, 2 + j * 5, 3.5f, 3.5f);
          using (var pen = new Pen(Color.FromArgb(30, 160, 60), 2.6f))
            g.DrawLines(pen, new[] { new PointF(12, 17), new PointF(16, 21), new PointF(23, 11) });
        }
        else
        {
          using (var br = new SolidBrush(Color.FromArgb(120, 120, 120)))
            for (int i = 0; i < 6; i++) for (int j = 0; j < 6; j++) g.FillEllipse(br, 1.5f + i * 3.6f, 1.5f + j * 3.6f, 2.4f, 2.4f);
        }
      }
      return bmp;
    }
  }
}
