using System;
using System.Windows;
using System.Windows.Media;

namespace AntField.Desktop;

// Build the textured entrance once; reuse the frozen drawing on every frame.
internal static class NestPainter
{
    static DrawingImage entrance = CreateEntrance(1);
    static int entranceStep;

    internal static void Draw(DrawingContext dc, Point home, double exitScale = 1)
    {
        int step = (int)Math.Floor((exitScale - 1) * 1000);
        if (step != entranceStep) { entrance = CreateEntrance(1 + step / 1000.0); entranceStep = step; }
        dc.DrawImage(entrance, new Rect(home.X - 48, home.Y - 24, 96, 46));
    }

    static DrawingImage CreateEntrance(double exitScale)
    {
        var drawing = new DrawingGroup();
        var random = new Random(731);
        using (var dc = drawing.Open())
        {
            // Fix the sprite bounds while leaving its perimeter fully transparent.
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 96, 46));
            var soil = Outline(48, 25, 34, 13, 37, .16, random);
            var shade = new RadialGradientBrush
            {
                Center = new Point(.5, .5), GradientOrigin = new Point(.5, .4),
                RadiusX = .5, RadiusY = .5
            };
            shade.GradientStops.Add(new GradientStop(Color.FromArgb(65, 0, 0, 0), .2));
            shade.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            dc.DrawEllipse(shade, null, new Point(49, 28), 44, 17);
            dc.DrawGeometry(new LinearGradientBrush(Color.FromRgb(132, 113, 80), Color.FromRgb(68, 53, 36), 90), null, soil);

            dc.PushClip(soil);
            // Uneven excavated clods, each lit from the upper left.
            for (int i = 0; i < 95; i++)
            {
                double angle = random.NextDouble() * Math.PI * 2;
                double radius = .45 + random.NextDouble() * .65;
                double x = 48 + Math.Cos(angle) * 32 * radius;
                double y = 24 + Math.Sin(angle) * 12 * radius;
                double size = .7 + random.NextDouble() * 2.6;
                byte tone = (byte)random.Next(82, 153);
                var clod = Outline(x, y, size, size * .65, 7, .3, random);
                dc.DrawGeometry(new LinearGradientBrush(Color.FromRgb(tone, (byte)(tone * .84), (byte)(tone * .6)), Color.FromRgb(60, 46, 31), 65), null, clod);
            }
            for (int i = 0; i < 650; i++)
            {
                double x = 10 + random.NextDouble() * 76, y = 9 + random.NextDouble() * 33;
                byte tone = (byte)random.Next(60, 183);
                double size = .12 + random.NextDouble() * .46;
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)random.Next(70, 170), tone, (byte)(tone * .85), (byte)(tone * .64))), null, new Point(x, y), size, size * .65);
            }

            // Three uneven entrances share the same low mound of excavated soil.
            DrawHole(dc, random, 48, 24, 10.5 * (1 + (exitScale - 1) * .5), 4.5 * exitScale);
            DrawHole(dc, random, 24, 28, 7 * (1 + (exitScale - 1) * .5), 3.5 * exitScale);
            DrawHole(dc, random, 71, 28.5, 7.5 * (1 + (exitScale - 1) * .5), 3.4 * exitScale);
            dc.Pop();

            // A few loose grains break up the silhouette rather than forming a badge.
            for (int i = 0; i < 35; i++)
            {
                double angle = random.NextDouble() * Math.PI * 2;
                double radius = .98 + random.NextDouble() * .2;
                double x = 48 + Math.Cos(angle) * 34 * radius;
                double y = 25 + Math.Sin(angle) * 13 * radius;
                double size = .3 + random.NextDouble() * .7;
                dc.DrawEllipse(AntPainter.ColorBrush(i % 2 == 0 ? "#947c55" : "#59452f"), null, new Point(x, y), size, size * .6);
            }
        }
        drawing.Freeze();
        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }

    static void DrawHole(DrawingContext dc, Random random, double x, double y, double rx, double ry)
    {
        dc.DrawGeometry(new LinearGradientBrush(Color.FromRgb(58, 43, 28), Color.FromRgb(152, 124, 82), 90), null,
            Outline(x, y, rx + 3, ry + 1.8, 23, .12, random));
        dc.DrawGeometry(new LinearGradientBrush(Color.FromRgb(12, 10, 7), Color.FromRgb(43, 31, 19), 90), null,
            Outline(x, y - .6, rx, ry, 21, .13, random));
    }

    static StreamGeometry Outline(double x, double y, double rx, double ry, int count, double variation, Random random)
    {
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            for (int i = 0; i < count; i++)
            {
                double angle = i * Math.PI * 2 / count;
                double radius = 1 + (random.NextDouble() * 2 - 1) * variation;
                var point = new Point(x + Math.Cos(angle) * rx * radius, y + Math.Sin(angle) * ry * radius);
                if (i == 0) path.BeginFigure(point, true, true);
                else path.LineTo(point, true, false);
            }
        }
        geometry.Freeze();
        return geometry;
    }
}
