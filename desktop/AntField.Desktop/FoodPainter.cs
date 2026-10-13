using System;
using System.Windows;
using System.Windows.Media;
using AntField.Core;

namespace AntField.Desktop;

internal static class FoodPainter
{
    static readonly DrawingGroup[] Morsels = CreateMorsels();
    static DrawingGroup[] CreateMorsels()
    {
        var drawings = new DrawingGroup[4];
        for (int variant = 0; variant < drawings.Length; variant++)
        {
            var outline = new StreamGeometry();
            using (var path = outline.Open())
            {
                for (int i = 0; i < 32; i++)
                {
                    double angle = i * Math.PI / 16;
                    double radius = .86 + .06 * Math.Sin(angle * 3 + variant) + .04 * Math.Cos(angle * 5);
                    var p = new Point(Math.Cos(angle) * radius, Math.Sin(angle) * radius * .86);
                    if (i == 0) path.BeginFigure(p, true, true); else path.LineTo(p, true, false);
                }
            }
            outline.Freeze();
            var body = AntPainter.Frozen(new RadialGradientBrush(Color.FromRgb(228, 199, 114), Color.FromRgb(132, 86, 39)) { GradientOrigin = new Point(.3, .25) });
            var drawing = new DrawingGroup();
            using (var dc = drawing.Open())
            {
                dc.PushTransform(new TranslateTransform(.06, .08));
                dc.DrawGeometry(AntPainter.ColorBrush("#45000000"), null, outline); dc.Pop();
                dc.DrawGeometry(body, new Pen(AntPainter.ColorBrush("#b48445"), .025), outline);
                dc.PushClip(outline);
                var random = new Random(810 + variant);
                for (int i = 0; i < 38; i++)
                {
                    double x = random.NextDouble() * 1.7 - .85, y = random.NextDouble() * 1.5 - .75;
                    double radius = .025 + random.NextDouble() * .065;
                    dc.DrawEllipse(AntPainter.ColorBrush(i % 3 == 0 ? "#876130" : "#ecd696"), null, new Point(x, y), radius, radius * .65);
                }
                dc.Pop();
            }
            drawings[variant] = AntPainter.Frozen(drawing);
        }
        return drawings;
    }
    internal static void DrawLarge(DrawingContext dc, FoodPatch patch)
    {
        double radius = patch.Radius;
        dc.PushTransform(new MatrixTransform(new Matrix(radius, 0, 0, radius, patch.Position.X, patch.Position.Y)));
        dc.DrawDrawing(Morsels[(patch.Id & int.MaxValue) % Morsels.Length]); dc.Pop();
    }
}
