using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using AntField.Core;

namespace AntField.Desktop;

internal sealed class OverlaySurface : FrameworkElement
{
    readonly DesktopColony colony;
    static readonly Brush Crumb = AntPainter.ColorBrush("#d1dc97");
    static readonly Brush CrumbShadow = AntPainter.ColorBrush("#35000000");
    static readonly Brush[] Trails = CreateTrailBrushes();
    static readonly Pen WallPen = AntPainter.Frozen(new Pen(AntPainter.ColorBrush("#709ec29c"), 1.5) { DashStyle = DashStyles.Dash });
    readonly ContainerVisual scene = new(), ants = new();
    readonly DrawingVisual nest = new(), walls = new(), trails = new(), food = new();
    readonly Dictionary<int, AntVisual> workers = new();
    sealed class AntVisual
    {
        internal readonly DrawingVisual Visual = new();
        internal readonly MatrixTransform Transform = new();
        internal int Pose = -1;
        internal AntVisual() { Visual.Transform = Transform; }
    }
    bool showTrails, showWalls, trailsDirty = true, wallsDirty = true;
    Vec? drawnHome;
    string foodSignature = "";
    double trailTime = -1;
    int drawnExitStep = -1;
    public bool ShowTrails { get => showTrails; set { if (showTrails != value) { showTrails = value; trailsDirty = true; InvalidateVisual(); } } }
    public bool ShowWalls { get => showWalls; set { if (showWalls != value) { showWalls = value; wallsDirty = true; InvalidateVisual(); } } }
    Geometry? groundClip;
    IReadOnlyList<Wall>? clippedWalls, clippedAreas;
    double clippedWidth, clippedHeight;
    public Rect NestHitBox => new(colony.Home.X - 48, colony.Home.Y - 24, 96, 46);
    public OverlaySurface(DesktopColony colony)
    {
        this.colony = colony;
        // Preserve separate visual bounds so moving an ant does not invalidate
        // the nest, food, trails and the entire transparent desktop surface.
        scene.Children.Add(nest); scene.Children.Add(walls); scene.Children.Add(trails); scene.Children.Add(food); scene.Children.Add(ants);
        AddVisualChild(scene);
    }
    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => index == 0 ? scene : throw new ArgumentOutOfRangeException(nameof(index));
    static Brush[] CreateTrailBrushes()
    {
        var brushes = new Brush[71];
        for (int i = 0; i < brushes.Length; i++) brushes[i] = AntPainter.Frozen(new SolidColorBrush(Color.FromArgb((byte)i, 105, 213, 155)));
        return brushes;
    }
    Geometry GroundClip()
    {
        var areas = colony.FoodAreas.Count > 0 ? colony.FoodAreas : colony.Screens;
        if (groundClip != null && ReferenceEquals(clippedWalls, colony.Walls) && ReferenceEquals(clippedAreas, areas) && clippedWidth == colony.Width && clippedHeight == colony.Height) return groundClip;
        var windows = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var wall in colony.Walls) windows.Children.Add(new RectangleGeometry(new Rect(wall.X, wall.Y, wall.Width, wall.Height)));
        var display = new GeometryGroup { FillRule = FillRule.Nonzero };
        if (areas.Count == 0) display.Children.Add(new RectangleGeometry(new Rect(0, 0, colony.Width, colony.Height)));
        else foreach (var area in areas) display.Children.Add(new RectangleGeometry(new Rect(area.X, area.Y, area.Width, area.Height)));
        groundClip = AntPainter.Frozen(new CombinedGeometry(GeometryCombineMode.Exclude, display, windows));
        clippedWalls = colony.Walls; clippedAreas = areas; clippedWidth = colony.Width; clippedHeight = colony.Height;
        wallsDirty = trailsDirty = true;
        return groundClip;
    }
    protected override void OnRender(DrawingContext dc) { base.OnRender(dc); UpdateFrame(); }
    internal void UpdateFrame()
    {
        var clip = GroundClip();
        if (!ReferenceEquals(ants.Clip, clip)) ants.Clip = clip;
        int exitStep = (int)Math.Floor((colony.Development.ExitScale - 1) * 1000);
        if (drawnHome != colony.Home || drawnExitStep != exitStep)
        {
            using var dc = nest.RenderOpen();
            NestPainter.Draw(dc, new Point(colony.Home.X, colony.Home.Y), colony.Development.ExitScale); drawnHome = colony.Home; drawnExitStep = exitStep;
        }
        if (wallsDirty)
        {
            using var dc = walls.RenderOpen();
            if (ShowWalls) foreach (var wall in colony.Walls) dc.DrawRectangle(null, WallPen, new Rect(wall.X, wall.Y, wall.Width, wall.Height));
            wallsDirty = false;
        }
        if (trailsDirty || (ShowTrails && colony.Time - trailTime >= .1))
        {
            using var dc = trails.RenderOpen();
            if (ShowTrails) for (int i = 0; i < colony.Pheromones.Length; i++)
            {
                double strength = colony.Pheromones[i];
                if (strength < .25 || colony.Navigation.HomeDistance[i] < 0) continue;
                var c = colony.Navigation.Center(i);
                dc.DrawEllipse(Trails[(int)Math.Min(70, strength * 6)], null, new Point(c.X, c.Y), 10, 10);
            }
            trailTime = colony.Time; trailsDirty = false;
        }
        string signature = string.Join(';', colony.Foods.Select(f => $"{f.Id}:{f.Position.X:R}:{f.Position.Y:R}:{f.Amount}:{f.Capacity}"));
        if (signature != foodSignature)
        {
            using var dc = food.RenderOpen();
            foreach (var patch in colony.Foods)
            {
                if (patch.IsLarge) { FoodPainter.DrawLarge(dc, patch); continue; }
                for (int i = 0; i < Math.Min(patch.Amount, 14); i++)
                {
                    double angle = i * 2.4, radius = Math.Sqrt(i) * 2;
                    var position = new Point(patch.Position.X + Math.Cos(angle) * radius, patch.Position.Y + Math.Sin(angle) * radius);
                    dc.DrawEllipse(CrumbShadow, null, new Point(position.X + 1, position.Y + 1), 2.5, 2);
                    dc.DrawEllipse(Crumb, null, position, 2.3, 1.8);
                }
            }
            foodSignature = signature;
        }
        // Population only grows during normal simulation. Remove stale visuals
        // as well, so diagnostic scenes and future population changes stay valid.
        if (workers.Count > colony.Workers.Count)
        {
            var ids = colony.Workers.Select(a => a.Id).ToHashSet();
            foreach (int id in workers.Keys.Where(id => !ids.Contains(id)).ToArray()) { ants.Children.Remove(workers[id].Visual); workers.Remove(id); }
        }
        foreach (var ant in colony.Workers)
        {
            if (!workers.TryGetValue(ant.Id, out var drawing))
            {
                drawing = new AntVisual(); workers.Add(ant.Id, drawing); ants.Children.Add(drawing.Visual);
            }
            double opacity = ant.State == AntState.InNest ? 0 : 1;
            if (drawing.Visual.Opacity != opacity) drawing.Visual.Opacity = opacity;
            if (opacity == 0) continue;
            int pose = AntPainter.PoseIndex(ant.Distance, ant.HasFood);
            if (pose != drawing.Pose)
            {
                using var dc = drawing.Visual.RenderOpen(); dc.DrawDrawing(AntPainter.Sprites[pose]); drawing.Pose = pose;
            }
            double c = Math.Cos(ant.Heading) * .85, s = Math.Sin(ant.Heading) * .85;
            var matrix = new Matrix(c, s, -s, c, ant.Position.X, ant.Position.Y);
            if (drawing.Transform.Matrix != matrix) drawing.Transform.Matrix = matrix;
        }
    }
}
