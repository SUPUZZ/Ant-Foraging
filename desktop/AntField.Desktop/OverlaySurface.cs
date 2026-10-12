using System;
using System.Windows;
using System.Windows.Media;
using AntField.Core;

namespace AntField.Desktop;

internal sealed class OverlaySurface : FrameworkElement
{
    readonly DesktopColony colony;
    static readonly Brush Crumb = AntPainter.ColorBrush("#d1dc97"), Nest = AntPainter.ColorBrush("#786347"), Entrance = AntPainter.ColorBrush("#201d14"), Badge = AntPainter.ColorBrush("#e4e9c9");
    static readonly Pen WallPen = AntPainter.Frozen(new Pen(AntPainter.ColorBrush("#709ec29c"), 1.5) { DashStyle = DashStyles.Dash });
    public bool ShowTrails { get; set; }
    public bool ShowWalls { get; set; }
    public Rect NestHitBox => new(colony.Home.X - 48, colony.Home.Y - 24, 96, 46);
    public OverlaySurface(DesktopColony colony) { this.colony = colony; }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ShowWalls) foreach (var wall in colony.Walls) dc.DrawRectangle(null, WallPen, new Rect(wall.X, wall.Y, wall.Width, wall.Height));
        if (ShowTrails) for (int i = 0; i < colony.Pheromones.Length; i++)
        {
            double strength = colony.Pheromones[i];
            if (strength < .25 || colony.Navigation.HomeDistance[i] < 0) continue;
            var c = colony.Navigation.Center(i);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)Math.Min(70, strength * 6), 105, 213, 155)), null, new Point(c.X, c.Y), 10, 10);
        }
        foreach (var food in colony.Foods)
        {
            for (int i = 0; i < Math.Min(food.Amount, 14); i++)
            {
                double angle = i * 2.4, radius = Math.Sqrt(i) * 2;
                dc.DrawEllipse(AntPainter.ColorBrush("#35000000"), null, new Point(food.Position.X + Math.Cos(angle) * radius + 1, food.Position.Y + Math.Sin(angle) * radius + 1), 2.5, 2);
                dc.DrawEllipse(Crumb, null, new Point(food.Position.X + Math.Cos(angle) * radius, food.Position.Y + Math.Sin(angle) * radius), 2.3, 1.8);
            }
        }
        var windows = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var wall in colony.Walls) windows.Children.Add(new RectangleGeometry(new Rect(wall.X, wall.Y, wall.Width, wall.Height)));
        var display = new GeometryGroup { FillRule = FillRule.Nonzero };
        var visibleAreas = colony.FoodAreas.Count > 0 ? colony.FoodAreas : colony.Screens;
        if (visibleAreas.Count == 0) display.Children.Add(new RectangleGeometry(new Rect(0, 0, colony.Width, colony.Height)));
        else foreach (var area in visibleAreas) display.Children.Add(new RectangleGeometry(new Rect(area.X, area.Y, area.Width, area.Height)));
        var groundClip = new CombinedGeometry(GeometryCombineMode.Exclude, display, windows);
        groundClip.Freeze(); dc.PushClip(groundClip);
        foreach (var ant in colony.Workers)
        {
            if (ant.State == AntState.InNest) continue;
            AntPainter.Draw(dc, new Point(ant.Position.X, ant.Position.Y), ant.Heading, .85, ant.Distance, colony.Time + ant.Id, ant.HasFood, true, 0);
        }
        dc.Pop();
        var home = colony.Home;
        dc.DrawEllipse(AntPainter.ColorBrush("#48000000"), null, new Point(home.X, home.Y + 6), 46, 15);
        dc.DrawEllipse(Nest, new Pen(AntPainter.ColorBrush("#b7a275"), 1), new Point(home.X, home.Y + 2), 40, 16);
        dc.DrawEllipse(Entrance, null, new Point(home.X, home.Y - 1), 17, 7);
        AntPainter.Text(dc, "蚁窝 · 点击查看", new Point(home.X - 42, home.Y + 8), 10, Badge, VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }
}
