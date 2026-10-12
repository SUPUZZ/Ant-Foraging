using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AntField.Core;

namespace AntField.Desktop;

internal sealed class NestView : FrameworkElement
{
    readonly DesktopColony colony;
    readonly Point[] centers = [new(180, 205), new(215, 380), new(650, 215), new(620, 380)];
    readonly string[] names = ["蚁后室", "育幼室", "粮仓", "工蚁休息区"];
    public int Selected { get; private set; } = 1;
    public event Action<int>? ChamberSelected;
    public NestView(DesktopColony colony)
    {
        this.colony = colony; Height = 380; Cursor = Cursors.Hand;
        MouseLeftButtonDown += (_, e) => {
            var p = e.GetPosition(this); double s = Math.Min(ActualWidth / 850, ActualHeight / 460), offset = (ActualWidth - 850 * s) / 2;
            int index = Enumerable.Range(0, 4).OrderBy(i => (centers[i] - new Point((p.X - offset) / s, p.Y / s)).Length).First();
            Selected = index; ChamberSelected?.Invoke(index); InvalidateVisual();
        };
    }
    protected override void OnRender(DrawingContext dc)
    {
        if (ActualWidth <= 0) return;
        double scale = Math.Min(ActualWidth / 850, ActualHeight / 460);
        dc.PushTransform(new TranslateTransform((ActualWidth - 850 * scale) / 2, 0));
        dc.PushTransform(new ScaleTransform(scale, scale));
        var earth = new LinearGradientBrush(AntPainter.ColorBrush("#67553a").ColorValue(), AntPainter.ColorBrush("#302c22").ColorValue(), 90);
        dc.DrawRoundedRectangle(earth, null, new Rect(0, 35, 850, 420), 16, 16);
        var rng = new Random(42);
        for (int i = 0; i < 320; i++) dc.DrawEllipse(AntPainter.ColorBrush("#28c1a77d"), null, new Point(rng.NextDouble() * 850, 46 + rng.NextDouble() * 400), 1 + rng.NextDouble(), 1);
        dc.DrawLine(new Pen(AntPainter.ColorBrush("#a5b97d"), 3), new Point(10, 35), new Point(840, 35));
        dc.DrawEllipse(AntPainter.ColorBrush("#837052"), null, new Point(425, 35), 40, 13);
        dc.DrawEllipse(AntPainter.ColorBrush("#191f17"), null, new Point(425, 35), 18, 6);
        AntPainter.Text(dc, "地表巢口", new Point(380, 4), 13, AntPainter.ColorBrush("#dce7c8"));
        var tunnel = new Pen(AntPainter.ColorBrush("#1f2119"), 22) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        dc.DrawLine(tunnel, new Point(425, 42), new Point(425, 310));
        for (int i = 0; i < centers.Length; i++)
        {
            var geometry = new StreamGeometry();
            using (var g = geometry.Open()) { g.BeginFigure(new Point(425, i % 2 == 0 ? 130 : 285), false, false); g.BezierTo(new Point(i < 2 ? 320 : 540, i % 2 == 0 ? 150 : 340), new Point(centers[i].X, centers[i].Y - 45), centers[i], true, false); }
            dc.DrawGeometry(null, tunnel, geometry);
            dc.DrawEllipse(AntPainter.ColorBrush("#26291e"), new Pen(AntPainter.ColorBrush(i == Selected ? "#c9d9a0" : "#897855"), i == Selected ? 2 : 1), centers[i], i == 0 ? 101 : 115, i == 0 ? 57 : 58);
            AntPainter.Text(dc, names[i], new Point(centers[i].X - 40, centers[i].Y - 42), 13, AntPainter.ColorBrush("#cbd8ab"));
        }
        // Queen and attendants stay inside the queen chamber.
        AntPainter.Draw(dc, new Point(170, 212), .1, 2.5, 0, colony.Time, false, false);
        for (int i = 0; i < 3; i++) AntPainter.Draw(dc, new Point(190 + Math.Sin(colony.Time * .3 + i * 2) * 58, 224 + Math.Cos(colony.Time * .3 + i * 2) * 13), colony.Time * .3 + i, .55, colony.Time * 3, colony.Time, false);
        int item = 0;
        foreach (var brood in colony.Nursery)
        {
            double x = 153 + item % 7 * 18, y = 375 + item / 7 * 15;
            var brush = AntPainter.ColorBrush(brood.Stage == BroodStage.Egg ? "#e6e2c3" : brood.Stage == BroodStage.Larva ? "#c7c397" : "#a9a16c");
            dc.DrawEllipse(brush, null, new Point(x, y), brood.Stage == BroodStage.Egg ? 3 : 5, brood.Stage == BroodStage.Larva ? 3 : 6); item++;
        }
        for (int i = 0; i < Math.Min(colony.Stored, 70); i++)
            dc.DrawEllipse(AntPainter.ColorBrush("#bfd082"), null, new Point(585 + (i % 14) * 9, 206 + i / 14 * 9), 3.4, 2.8);
        var inside = colony.Workers.Where(a => a.State == AntState.InNest).Take(18).ToArray();
        for (int i = 0; i < inside.Length; i++) AntPainter.Draw(dc, new Point(544 + i % 6 * 29, 375 + i / 6 * 15), inside[i].Heading, .75, 0, colony.Time + i, false, false);
        int traffic = 0;
        foreach (var ant in colony.Workers.Where(a => a.State == AntState.Returning || a.State == AntState.InNest).Take(10))
        {
            double t = (colony.Time * 13 + ant.Id * 31) % 200;
            AntPainter.Draw(dc, new Point(425 + (traffic % 2 == 0 ? -3 : 4), 80 + t), traffic % 2 == 0 ? Math.PI / 2 : -Math.PI / 2, .55, colony.Time * 12, colony.Time, ant.HasFood); traffic++;
        }
        AntPainter.Text(dc, $"卵 {colony.Nursery.Count(b => b.Stage == BroodStage.Egg)} · 幼虫 {colony.Nursery.Count(b => b.Stage == BroodStage.Larva)} · 蛹 {colony.Nursery.Count(b => b.Stage == BroodStage.Pupa)}", new Point(110, 425), 11, AntPainter.ColorBrush("#b2bb96"));
        AntPainter.Text(dc, $"储粮 {colony.Stored} · 巢内工蚁 {colony.Workers.Count(a => a.State == AntState.InNest)}", new Point(530, 425), 11, AntPainter.ColorBrush("#b2bb96"));
        dc.Pop(); dc.Pop();
    }
}
internal static class BrushExtensions
{
    internal static Color ColorValue(this Brush brush) => ((SolidColorBrush)brush).Color;
}
