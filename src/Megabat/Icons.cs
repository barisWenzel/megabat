using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Megabat
{
  // Icons are drawn at runtime: coloured tile per group with abbreviation
  public static class Icons
  {
    public static readonly Color Cloud = Color.FromArgb(52, 101, 164);
    public static readonly Color Segments = Color.FromArgb(78, 154, 6);
    public static readonly Color Elements = Color.FromArgb(206, 92, 0);
    public static readonly Color Section = Color.FromArgb(117, 80, 123);
    public static readonly Color Display = Color.FromArgb(85, 87, 83);

    static readonly Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>();

    public static Bitmap For(string text, Color back)
    {
      string key = text + back.ToArgb();
      if (cache.TryGetValue(key, out var bmp)) return bmp;
      bmp = new Bitmap(24, 24);
      using (var g = Graphics.FromImage(bmp))
      {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);
        using (var path = Rounded(new RectangleF(1, 1, 22, 22), 5))
        using (var br = new SolidBrush(back))
          g.FillPath(br, path);
        float size = text.Length <= 2 ? 9.5f : 7f;
        using (var f = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Point))
        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
          g.DrawString(text, f, Brushes.White, new RectangleF(0, 0, 24, 25), sf);
      }
      cache[key] = bmp;
      return bmp;
    }

    // Logo: bat silhouette from simple shapes
    public static Bitmap Logo(int s = 24)
    {
      var bmp = new Bitmap(s, s);
      using (var g = Graphics.FromImage(bmp))
      {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        float k = s / 24f;
        var pts = new[] {
          new PointF(1, 9), new PointF(6, 7), new PointF(9, 10), new PointF(10.5f, 6), new PointF(12, 8),
          new PointF(13.5f, 6), new PointF(15, 10), new PointF(18, 7), new PointF(23, 9),
          new PointF(19, 12), new PointF(17, 16), new PointF(14, 14), new PointF(12, 18),
          new PointF(10, 14), new PointF(7, 16), new PointF(5, 12) };
        for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(pts[i].X * k, pts[i].Y * k);
        using (var br = new SolidBrush(Color.FromArgb(40, 40, 48))) g.FillPolygon(br, pts);
        using (var br = new SolidBrush(Color.FromArgb(230, 180, 40)))
          for (int i = 0; i < 4; i++) g.FillEllipse(br, (4 + i * 5) * k, 19.5f * k, 2.2f * k, 2.2f * k);
      }
      return bmp;
    }

    static GraphicsPath Rounded(RectangleF r, float rad)
    {
      var p = new GraphicsPath();
      float d = rad * 2;
      p.AddArc(r.X, r.Y, d, d, 180, 90);
      p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
      p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
      p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
      p.CloseFigure();
      return p;
    }
  }
}
