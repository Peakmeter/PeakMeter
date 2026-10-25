using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Draws the PeakMeter logo at 1024 px and writes PNGs and a multi-size .ico.
static class Program
{
    static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    static Geometry Poly(params double[] p)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(p[0], p[1]), true, true);
            for (var i = 2; i < p.Length; i += 2) c.LineTo(new Point(p[i], p[i + 1]), true, true);
        }
        g.Freeze();
        return g;
    }

    static DrawingVisual Draw(bool small)
    {
        var v = new DrawingVisual();
        using var dc = v.RenderOpen();
        const double S = 1024;

        // Tile: deep navy, lighter at the top, thin edge.
        var tile = new RectangleGeometry(new Rect(0, 0, S, S), 228, 228);
        dc.DrawGeometry(new LinearGradientBrush(C("#1E2440"), C("#090B13"), 90), null, tile);
        dc.PushClip(tile);
        // soft glow behind the summit
        var glow = new RadialGradientBrush(Color.FromArgb(110, 0x7C, 0x9C, 0xFF), Color.FromArgb(0, 0x7C, 0x9C, 0xFF))
        { Center = new Point(0.5, 0.36), GradientOrigin = new Point(0.5, 0.36), RadiusX = 0.55, RadiusY = 0.5 };
        dc.DrawRectangle(glow, null, new Rect(0, 0, S, S));

        double baseY = small ? 830 : 812;
        // Back peak (right), darker violet.
        dc.DrawGeometry(new LinearGradientBrush(C("#6B4FD8"), C("#2E2470"), 90), null,
            Poly(560, baseY, 760, small ? 470 : 440, 930, baseY));
        // Main peak: lit left face, shaded right face.
        double sx = 470, sy = small ? 170 : 190, lx = 110, rx = 830;
        dc.DrawGeometry(new LinearGradientBrush(C("#B8CBFF"), C("#6F8CFF"), 90), null, Poly(lx, baseY, sx, sy, 520, baseY));
        dc.DrawGeometry(new LinearGradientBrush(C("#8C6CFF"), C("#4A33B8"), 90), null, Poly(520, baseY, sx, sy, rx, baseY));
        if (!small)
        {
            // Snow line on the summit.
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)), null,
                Poly(sx, sy, 400, 318, 445, 296, 482, 330, 521, 300, 548, 332));
            // Ground line.
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), null, new Rect(90, baseY, 860, 6));
        }
        // Spark above the summit: four-point star.
        double cx = small ? 735 : 720, cy = small ? 250 : 235, r = small ? 135 : 105, w = small ? 34 : 24;
        var star = Poly(cx, cy - r, cx + w, cy - w, cx + r, cy, cx + w, cy + w, cx, cy + r, cx - w, cy + w, cx - r, cy, cx - w, cy - w);
        dc.DrawGeometry(new RadialGradientBrush(Color.FromArgb(120, 255, 255, 255), Color.FromArgb(0, 255, 255, 255)), null,
            new EllipseGeometry(new Point(cx, cy), r * 1.25, r * 1.25));
        dc.DrawGeometry(Brushes.White, null, star);
        dc.Pop();
        // Edge highlight.
        dc.DrawGeometry(null, new Pen(new LinearGradientBrush(Color.FromArgb(70, 255, 255, 255), Color.FromArgb(10, 255, 255, 255), 90), 10),
            new RectangleGeometry(new Rect(5, 5, S - 10, S - 10), 223, 223));
        return v;
    }

    static byte[] Png(BitmapSource src, int size)
    {
        var scaled = new TransformedBitmap(src, new ScaleTransform(size / (double)src.PixelWidth, size / (double)src.PixelHeight));
        // High-quality downscale through a drawing at the target size.
        var dv = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.Fant);
        using (var dc = dv.RenderOpen()) dc.DrawImage(src, new Rect(0, 0, size, size));
        var rt = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rt.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rt));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    static BitmapSource Render(bool small)
    {
        var rt = new RenderTargetBitmap(1024, 1024, 96, 96, PixelFormats.Pbgra32);
        rt.Render(Draw(small));
        rt.Freeze();
        return rt;
    }

    [STAThread]
    static void Main(string[] args)
    {
        var outDir = args[0];
        var big = Render(false);
        var small = Render(true); // simpler shapes that survive 16-32 px
        File.WriteAllBytes(Path.Combine(outDir, "logo-1024.png"), Png(big, 1024));
        File.WriteAllBytes(Path.Combine(outDir, "logo-256.png"), Png(big, 256));
        File.WriteAllBytes(Path.Combine(outDir, "logo-32.png"), Png(small, 32));

        int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
        var images = sizes.Select(s => Png(s <= 48 ? small : big, s)).ToList();
        using var ico = new BinaryWriter(File.Create(Path.Combine(outDir, "app.ico")));
        ico.Write((short)0); ico.Write((short)1); ico.Write((short)sizes.Length);
        var offset = 6 + 16 * sizes.Length;
        for (var i = 0; i < sizes.Length; i++)
        {
            ico.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); ico.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            ico.Write((byte)0); ico.Write((byte)0); ico.Write((short)1); ico.Write((short)32);
            ico.Write(images[i].Length); ico.Write(offset);
            offset += images[i].Length;
        }
        foreach (var img in images) ico.Write(img);
        Console.WriteLine("ok");
    }
}
