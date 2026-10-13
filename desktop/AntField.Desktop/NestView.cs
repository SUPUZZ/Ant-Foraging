using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using AntField.Core;

namespace AntField.Desktop;

internal sealed class NestView : Grid
{
    static readonly ControlTemplate RoomTemplate = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
          <Border x:Name="border" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="2">
            <ContentPresenter Margin="{TemplateBinding Padding}" HorizontalAlignment="Center" VerticalAlignment="Center" />
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsEnabled" Value="False"><Setter TargetName="border" Property="Opacity" Value="0.4" /></Trigger>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="border" Property="Opacity" Value="0.8" /></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """);
    static Button LayerButton(string text) => new() { Content = text, Template = RoomTemplate, Padding = new Thickness(6), Margin = new Thickness(2), Background = AntPainter.ColorBrush("#293b30"), Foreground = AntPainter.ColorBrush("#ccd8be"), BorderBrush = AntPainter.ColorBrush("#53654c"), BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
    readonly DesktopColony colony;
    readonly Viewport3D viewport = new();
    readonly PerspectiveCamera camera = new() { FieldOfView = 43, NearPlaneDistance = .1, FarPlaneDistance = 400 };
    readonly Model3DGroup inhabitants = new(), supplies = new(), brood = new();
    readonly Dictionary<int, (Model3DGroup Model, Worker Worker, int Chamber)> workers = new();
    readonly Button[] roomButtons = new Button[4];
    GeometryModel3D terrain;
    readonly Model3DGroup cutaway = new();
    readonly TextBlock layerText = new() { Foreground = AntPainter.ColorBrush("#ccd8be"), Margin = new Thickness(8, 7, 8, 0) };
    Button shallower = null!, deeper = null!;
    NestStratum? stratum;
    int excavationStep;
    public long Layer { get; private set; }
    readonly Model3DGroup queen;
    readonly Model3DGroup selection = new();
    Point pressed, previous;
    bool dragging, rendering;
    double yaw = 22, pitch = 20, distance = 28, renderedTime = -1;
    string broodStages = "";
    int stored = -1;
    public int Selected { get; private set; } = 1;
    public event Action<int>? ChamberSelected;
    internal int VisibleQueens => inhabitants.Children.Contains(queen) ? 1 : 0;
    internal (int Workers, int Food, int Brood) VisibleCounts => (workers.Count, supplies.Children.Count, brood.Children.Count);

    public NestView(DesktopColony colony)
    {
        this.colony = colony;
        Height = 420; ClipToBounds = true;
        Background = new LinearGradientBrush(System.Windows.Media.Color.FromRgb(17, 26, 23), System.Windows.Media.Color.FromRgb(39, 50, 42), 90);
        viewport.Camera = camera; viewport.Margin = new Thickness(10, 8, 10, 52); Children.Add(viewport);
        viewport.SizeChanged += (_, _) => UpdateCamera();
        var scene = new Model3DGroup();
        scene.Children.Add(new AmbientLight(System.Windows.Media.Color.FromRgb(105, 105, 100)));
        scene.Children.Add(new DirectionalLight(System.Windows.Media.Color.FromRgb(255, 238, 209), new Vector3D(-.4, -.7, -1)));
        scene.Children.Add(new DirectionalLight(System.Windows.Media.Color.FromRgb(111, 135, 127), new Vector3D(.8, .1, -.6)));
        terrain = NestGeometry3D.CreateTerrain(); cutaway.Children.Add(terrain);
        cutaway.Children.Add(NestGeometry3D.CreateSoilBlock());
        cutaway.Children.Add(NestGeometry3D.CreateSurface()); scene.Children.Add(cutaway);
        scene.Children.Add(selection);
        scene.Children.Add(supplies); scene.Children.Add(brood); scene.Children.Add(inhabitants);
        queen = new Model3DGroup(); queen.Children.Add(NestGeometry3D.Queen);
        var queenPosition = NestGeometry3D.ChamberPoint(0, -.12, -.08);
        queen.Transform = Placement(queenPosition.X, queenPosition.Y, 12, 1.15);
        inhabitants.Children.Add(queen);
        viewport.Children.Add(new ModelVisual3D { Content = scene });

        var rooms = new UniformGrid { Rows = 1, Columns = 4, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12, 0, 12, 10) };
        for (int i = 0; i < 4; i++)
        {
            int index = i;
            var button = new Button { Template = RoomTemplate, Content = NestGeometry3D.Names[i], Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(3),
                Foreground = AntPainter.ColorBrush("#e1e5ce"), BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
            button.Click += (_, _) => SelectChamber(index);
            System.Windows.Automation.AutomationProperties.SetName(button, $"查看{NestGeometry3D.Names[i]}");
            roomButtons[i] = button; rooms.Children.Add(button);
        }
        Children.Add(rooms);
        var reset = new Button { Content = "复位视角", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(12), Padding = new Thickness(10, 5, 10, 5), Background = AntPainter.ColorBrush("#293b30"), Foreground = AntPainter.ColorBrush("#ccd8be"), BorderBrush = AntPainter.ColorBrush("#53654c") };
        reset.Click += (_, _) => { yaw = 22; pitch = 20; distance = 28; UpdateCamera(); };
        Children.Add(reset);
        var layers = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12) };
        shallower = LayerButton("↑ 上层");
        deeper = LayerButton("↓ 下层");
        shallower.Click += (_, _) => ShowLayer(Layer - 1); deeper.Click += (_, _) => ShowLayer(Layer + 1);
        var deepest = LayerButton("开掘前沿");
        deepest.Click += (_, _) => ShowLayer(colony.Development.DeepestLayer);
        layers.Children.Add(shallower); layers.Children.Add(layerText); layers.Children.Add(deeper); layers.Children.Add(deepest); Children.Add(layers);
        viewport.MouseLeftButtonDown += (_, e) => { pressed = previous = e.GetPosition(viewport); dragging = false; viewport.CaptureMouse(); e.Handled = true; };
        viewport.MouseMove += (_, e) => {
            if (!viewport.IsMouseCaptured) return;
            var point = e.GetPosition(viewport);
            if ((point - pressed).Length > 4) dragging = true;
            if (dragging) { yaw = Math.Clamp(yaw + (point.X - previous.X) * .15, -25, 25); pitch = Math.Clamp(pitch + (point.Y - previous.Y) * .12, -5, 25); UpdateCamera(); }
            previous = point;
        };
        viewport.MouseLeftButtonUp += (_, e) => { if (!viewport.IsMouseCaptured) return; viewport.ReleaseMouseCapture(); if (!dragging) SelectAt(e.GetPosition(viewport)); e.Handled = true; };
        viewport.MouseWheel += (_, e) => { distance = Math.Clamp(distance - e.Delta / 120.0 * 1.2, 20, 36); UpdateCamera(); e.Handled = true; };
        Loaded += (_, _) => UpdateRendering();
        Unloaded += (_, _) => StopRendering();
        IsVisibleChanged += (_, _) => UpdateRendering();
        UpdateCamera(); UpdateSelection(); RefreshScene();
    }

    void UpdateCamera()
    {
        double a = yaw * Math.PI / 180, b = pitch * Math.PI / 180;
        double fitted = distance * Math.Max(1, viewport.ActualWidth / Math.Max(1, viewport.ActualHeight) / 2.3);
        var position = new Point3D(fitted * Math.Sin(a) * Math.Cos(b), .5 + fitted * Math.Sin(b), fitted * Math.Cos(a) * Math.Cos(b));
        camera.Position = position; camera.LookDirection = new Point3D(0, .1, -.7) - position; camera.UpDirection = new Vector3D(0, 1, 0);
    }

    internal bool SelectAt(Point point)
    {
        bool selected = false;
        // Pick the cut surface rather than selecting whichever room is nearest.
        VisualTreeHelper.HitTest(viewport, null, result => {
            if (result is RayMeshGeometry3DHitTestResult hit && ReferenceEquals(hit.ModelHit, terrain))
            {
                for (int i = 0; i < 4; i++) if (stratum == null ? NestGeometry3D.InChamber(i, hit.PointHit.X, hit.PointHit.Y) : stratum.InChamber(i, hit.PointHit.X, hit.PointHit.Y)) { SelectChamber(i); selected = true; break; }
                return HitTestResultBehavior.Stop;
            }
            return HitTestResultBehavior.Continue;
        }, new PointHitTestParameters(point));
        return selected;
    }

    void SelectChamber(int index) { Selected = index; UpdateSelection(); ChamberSelected?.Invoke(index); }

    void UpdateSelection()
    {
        selection.Children.Clear(); if (stratum == null) selection.Children.Add(NestGeometry3D.ChamberRim(Selected));
        else if (Selected < Math.Floor(stratum.Excavated)) selection.Children.Add(stratum.Rim(Selected));
        for (int i = 0; i < 4; i++)
        {
            roomButtons[i].Content = stratum == null ? NestGeometry3D.Names[i] : new[] { "扩建育幼室", "扩建休息室", "扩建粮仓", "深层休息室" }[i];
            System.Windows.Automation.AutomationProperties.SetName(roomButtons[i], $"查看{roomButtons[i].Content}");
            roomButtons[i].IsEnabled = stratum == null || i < Math.Floor(stratum.Excavated);
            roomButtons[i].Background = AntPainter.ColorBrush(i == Selected ? "#475339" : "#203127");
            roomButtons[i].BorderBrush = AntPainter.ColorBrush(i == Selected ? "#b8ca8c" : "#435640");
        }
    }

    internal void ShowLayer(long layer)
    {
        Layer = Math.Clamp(layer, 0, colony.Development.DeepestLayer); excavationStep = -1;
        RefreshScene(); ChamberSelected?.Invoke(Selected);
    }
    void RefreshCutaway()
    {
        layerText.Text = Layer == 0 ? "主巢 · 蚁后 1" : $"地下第 {Layer} 层";
        shallower.IsEnabled = Layer > 0; deeper.IsEnabled = Layer < colony.Development.DeepestLayer;
        int step = Layer == 0 ? 0 : (int)Math.Floor(colony.Development.RoomsInLayer(Layer) * 20);
        if (step == excavationStep) return;
        excavationStep = step; stratum = Layer == 0 ? null : new NestStratum(Layer, step / 20.0);
        cutaway.Children.Clear(); terrain = stratum == null ? NestGeometry3D.CreateTerrain() : NestGeometry3D.CreateTerrain(stratum.Depth);
        cutaway.Children.Add(terrain); cutaway.Children.Add(NestGeometry3D.CreateSoilBlock());
        if (stratum == null) cutaway.Children.Add(NestGeometry3D.CreateSurface());
        if (stratum == null && !inhabitants.Children.Contains(queen)) inhabitants.Children.Add(queen);
        if (stratum != null) inhabitants.Children.Remove(queen);
        stored = -1; broodStages = "!"; UpdateSelection();
    }
    Point ChamberPoint(int chamber, double x, double y) => stratum == null ? NestGeometry3D.ChamberPoint(chamber, x, y) : stratum.Centers[chamber] + new Vector(x * 1.2, y * .65);
    double Depth(double x, double y) => stratum == null ? NestGeometry3D.Depth(x, y) : stratum.Depth(x, y);
    public void RefreshScene()
    {
        RefreshCutaway();
        int completed = stratum == null ? 4 : (int)Math.Floor(stratum.Excavated);
        long total = colony.Development.CompletedRooms;
        long foodLayers = 1 + total / 4 + (total % 4 >= 3 ? 1 : 0);
        long nurseryLayers = 1 + total / 4 + (total % 4 >= 1 ? 1 : 0);
        int visibleFood = completed >= 3 ? (int)Math.Min(70, colony.Stored / foodLayers + (Layer < colony.Stored % foodLayers ? 1 : 0)) : 0;
        var localBrood = colony.Nursery.Where((_, i) => i % nurseryLayers == Layer).ToArray();
        if (stored != visibleFood)
        {
            stored = visibleFood; supplies.Children.Clear();
            var random = new Random(320);
            for (int i = 0; i < visibleFood; i++)
            {
                double angle = random.NextDouble() * Math.PI * 2, radius = Math.Sqrt(random.NextDouble());
                var point = ChamberPoint(2, Math.Cos(angle) * radius * .65, Math.Sin(angle) * radius * .6);
                double x = point.X, y = point.Y;
                supplies.Children.Add(NestGeometry3D.Pellet(x, y, Depth(x, y) + .1, i));
            }
        }
        string stages = string.Join(',', localBrood.Select(b => (int)b.Stage));
        if (stages != broodStages)
        {
            broodStages = stages; brood.Children.Clear();
            for (int i = 0; i < (completed > 0 ? localBrood.Length : 0); i++)
            {
                double angle = i * 2.4, radius = .12 + Math.Sqrt(i) * .1;
                var point = ChamberPoint(stratum == null ? 1 : 0, Math.Cos(angle) * radius, Math.Sin(angle) * radius * .75);
                double x = point.X, y = point.Y;
                brood.Children.Add(NestGeometry3D.Brood(localBrood[i].Stage, x, y, Depth(x, y) + .11));
            }
        }
        var visible = colony.Workers.Where(a => a.State == AntState.InNest && a.Id % nurseryLayers == Layer).Take(completed == 0 ? 0 : 18).Select((ant, i) => (Ant: ant, Chamber: stratum == null ? (i < 2 ? 0 : i < 4 ? 1 : 3) : i % completed)).ToArray();
        var ids = visible.Select(a => a.Ant.Id).ToHashSet();
        foreach (int id in workers.Keys.Where(id => !ids.Contains(id)).ToArray()) { inhabitants.Children.Remove(workers[id].Model); workers.Remove(id); }
        foreach (var entry in visible)
        {
            if (workers.TryGetValue(entry.Ant.Id, out var existing)) { workers[entry.Ant.Id] = (existing.Model, entry.Ant, entry.Chamber); continue; }
            var model = new Model3DGroup { Transform = new MatrixTransform3D() }; model.Children.Add(NestGeometry3D.AntPoses[0]);
            inhabitants.Children.Add(model); workers.Add(entry.Ant.Id, (model, entry.Ant, entry.Chamber));
        }
        Animate();
    }

    void Animate()
    {
        double time = colony.Time;
        foreach (var entry in workers.Values)
        {
            double phase = time * .45 + entry.Worker.Id * 2.399;
            double rx = .32 + entry.Worker.Id % 7 * .04, ry = .32 + entry.Worker.Id % 4 * .06;
            var point = ChamberPoint(entry.Chamber, Math.Sin(phase) * rx, Math.Cos(phase * .9) * ry);
            double x = point.X, y = point.Y;
            var tangent = ChamberPoint(entry.Chamber, Math.Cos(phase) * rx, -.9 * Math.Sin(phase * .9) * ry) - (stratum == null ? NestGeometry3D.Centers[entry.Chamber] : stratum.Centers[entry.Chamber]);
            double angle = Math.Atan2(tangent.Y, tangent.X) * 180 / Math.PI;
            var pose = NestGeometry3D.AntPoses[(int)(time * 8 + entry.Worker.Id) % NestGeometry3D.AntPoses.Length];
            if (!ReferenceEquals(entry.Model.Children[0], pose)) entry.Model.Children[0] = pose;
            ((MatrixTransform3D)entry.Model.Transform).Matrix = PlacementMatrix(x, y, angle, .48);
        }
        renderedTime = time;
    }

    Transform3D Placement(double x, double y, double angle, double scale)
    {
        return AntPainter.Frozen(new MatrixTransform3D(PlacementMatrix(x, y, angle, scale)));
    }
    Matrix3D PlacementMatrix(double x, double y, double angle, double scale)
    {
        double radians = angle * Math.PI / 180, c = Math.Cos(radians) * scale, s = Math.Sin(radians) * scale;
        return new Matrix3D(c, s, 0, 0, -s, c, 0, 0, 0, 0, scale, 0, x, y, Depth(x, y) + .08, 1);
    }

    void UpdateRendering()
    {
        if (IsLoaded && IsVisible && !rendering) { CompositionTarget.Rendering += RenderFrame; rendering = true; }
        else if (!IsLoaded || !IsVisible) StopRendering();
    }
    void StopRendering() { if (rendering) { CompositionTarget.Rendering -= RenderFrame; rendering = false; } }
    void RenderFrame(object? sender, EventArgs e) { if (colony.Time != renderedTime) Animate(); }
}
