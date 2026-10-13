using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using AntField.Core;

namespace AntField.Desktop;

internal static class RenderingChecks
{
    internal static bool NestCutaway(string output)
    {
        NestConnectivity();
        var colony = new DesktopColony(800, 500, 6);
        foreach (var worker in colony.Workers) worker.State = AntState.InNest;
        var view = new NestView(colony);
        view.Measure(new Size(860, 420)); view.Arrange(new Rect(0, 0, 860, 420)); view.UpdateLayout();
        var viewport = view.Children.OfType<Viewport3D>().Single();
        var camera = (PerspectiveCamera)viewport.Camera;
        void CheckRooms()
        {
            var forward = camera.LookDirection; forward.Normalize();
            var right = Vector3D.CrossProduct(forward, camera.UpDirection); right.Normalize();
            var up = Vector3D.CrossProduct(right, forward);
            double focal = viewport.ActualWidth / (2 * Math.Tan(camera.FieldOfView * Math.PI / 360));
            for (int i = 0; i < 4; i++)
            {
                var center = NestGeometry3D.Centers[i];
                var delta = new Point3D(center.X, center.Y, NestGeometry3D.Depth(center.X, center.Y)) - camera.Position;
                double z = Vector3D.DotProduct(delta, forward);
                var pixel = new Point(viewport.ActualWidth / 2 + focal * Vector3D.DotProduct(delta, right) / z,
                    viewport.ActualHeight / 2 - focal * Vector3D.DotProduct(delta, up) / z);
                if (!view.SelectAt(pixel) || view.Selected != i) throw new InvalidOperationException("3D 巢室的射线点击与当前视角不匹配。");
            }
        }
        CheckRooms();
        camera.Position = new Point3D(-8, 6, 28); camera.LookDirection = new Point3D(0, .1, -.7) - camera.Position;
        CheckRooms();
        if (view.SelectAt(new Point(1, 1))) throw new InvalidOperationException("空白处错误选中了巢室。");
        if (view.VisibleCounts != (6, colony.Stored, colony.Nursery.Count)) throw new InvalidOperationException("3D 内部数量没有反映当前仿真。");
        colony.Workers[0].State = AntState.Searching; colony.Workers[1].State = AntState.Returning;
        colony.Nursery.Clear(); view.RefreshScene();
        if (view.VisibleCounts != (4, colony.Stored, 0)) throw new InvalidOperationException("3D 视图保留了已离巢的工蚁或育幼个体。");
        foreach (var room in view.Children.OfType<System.Windows.Controls.Primitives.UniformGrid>().Single().Children.OfType<Button>()) room.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (view.Selected != 3) throw new InvalidOperationException("巢室按钮未更新选择。");
        view.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        CheckRooms();
        view.Measure(new Size(1400, 420)); view.Arrange(new Rect(0, 0, 1400, 420)); view.UpdateLayout();
        view.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        CheckRooms();
        view.Measure(new Size(860, 420)); view.Arrange(new Rect(0, 0, 860, 420)); view.UpdateLayout();
        view.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        view.UpdateLayout();
        var bitmap = new RenderTargetBitmap(860, 420, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(output, "nest-cutaway-check.png")); encoder.Save(file);
        return true;
    }

    static void NestConnectivity()
    {
        const int width = 201, height = 109;
        var carved = new bool[width * height]; var visited = new bool[carved.Length];
        int Cell(double x, double y) => (int)Math.Round((y + 4.2) / 8.6 * (height - 1)) * width + (int)Math.Round((x + 8) / 16 * (width - 1));
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            carved[y * width + x] = NestGeometry3D.Depth(-8 + x * 16.0 / (width - 1), -4.2 + y * 8.6 / (height - 1)) < -.08;
        int entrance = Cell(0, 4.32);
        if (!carved[entrance]) throw new InvalidOperationException("The entrance does not open into the cutaway.");
        var queue = new System.Collections.Generic.Queue<int>(); queue.Enqueue(entrance); visited[entrance] = true;
        while (queue.TryDequeue(out int cell))
        {
            int x = cell % width, y = cell / width;
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                int next = ny * width + nx;
                if (!carved[next] || visited[next]) continue;
                visited[next] = true; queue.Enqueue(next);
            }
        }
        foreach (var center in NestGeometry3D.Centers)
            if (!visited[Cell(center.X, center.Y)]) throw new InvalidOperationException("A nest chamber is disconnected from the entrance.");
    }

    internal static bool WindowOcclusion()
    {
        NestLayering();
        var colony = new DesktopColony(800, 500, 3);
        colony.Workers[0].Position = new Vec(300, 220);
        colony.Workers[1].Position = new Vec(150, 150);
        colony.Workers[2].Position = new Vec(410, 290);
        foreach (var ant in colony.Workers) ant.State = AntState.Searching;
        colony.UpdateWalls([new Wall(1, 250, 150, 210, 190), new Wall(2, 385, 260, 180, 130)]);
        var view = new OverlaySurface(colony) { Width = colony.Width, Height = colony.Height };
        var ground = Render(colony, view);
        if (Visible(ground, colony.Workers[0].Position) || Visible(ground, colony.Workers[2].Position) || !Visible(ground, colony.Workers[1].Position))
            throw new InvalidOperationException("窗口下方或重叠窗口的遮挡绘制不正确。");
        foreach (var state in new[] { AntState.Climbing, AntState.OnWindow, AntState.Descending })
        {
            colony.Workers[0].State = state;
            if (Visible(Render(colony, view), colony.Workers[0].Position)) throw new InvalidOperationException("旧爬墙状态的工蚁显示在窗口表面。");
        }
        var position = colony.Workers[0].Position;
        colony.UpdateWalls([new Wall(1, 280, 180, 210, 190)]);
        if (colony.Workers[0].Position != position || Visible(Render(colony, view), position)) throw new InvalidOperationException("移动窗口后工蚁移位或显示在窗口上。");
        colony.Workers[0].State = AntState.UnderWindow;
        colony.UpdateWalls([]);
        if (!Visible(Render(colony, view), colony.Workers[0].Position)) throw new InvalidOperationException("移走窗口后未显露原位置的工蚁。");
        colony.FoodAreas = [new Wall(0, 0, 0, 100, 100)];
        if (Visible(Render(colony, view), colony.Workers[1].Position)) throw new InvalidOperationException("屏幕工作区域变化后遮挡缓存未更新。");
        colony.FoodAreas = [];
        if (!Visible(Render(colony, view), colony.Workers[1].Position)) throw new InvalidOperationException("恢复屏幕工作区域后工蚁仍被缓存遮挡。");
        return true;
    }
    internal static void LargeFood(string output)
    {
        var colony = new DesktopColony(800, 500, 1);
        var patch = new FoodPatch { Id = 10, Position = new Vec(200, 200), Amount = 200, Capacity = 200 };
        colony.Foods.Add(patch);
        colony.Foods.Add(new FoodPatch { Id = 11, Position = new Vec(310, 200), Amount = 12, Capacity = 12 });
        var ant = colony.Workers[0]; ant.State = AntState.Searching; ant.Position = new Vec(250, 200); ant.Heading = Math.PI;
        var view = new OverlaySurface(colony) { Width = 800, Height = 500 };
        var full = Render(colony, view);
        int Area(BitmapSource bitmap)
        {
            var pixels = new byte[64 * 64 * 4]; bitmap.CopyPixels(new Int32Rect(168, 168, 64, 64), pixels, 64 * 4, 0);
            int count = 0; for (int i = 3; i < pixels.Length; i += 4) if (pixels[i] > 30) count++; return count;
        }
        int original = Area(full); patch.Amount = 10;
        int depleted = Area(Render(colony, view));
        if (original < 900 || depleted >= original * .6) throw new InvalidOperationException("大食物没有显示为大块食物，或搬运后未缩小。");
        patch.Amount = 0; colony.Foods.Remove(patch);
        if (Area(Render(colony, view)) != 0) throw new InvalidOperationException("耗尽的大食物仍有残留绘制。");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(new CroppedBitmap(full, new Int32Rect(154, 152, 205, 96))));
        using var file = File.Create(Path.Combine(output, "large-food-check.png")); encoder.Save(file);
    }
    static void NestLayering()
    {
        // Regression: the opaque mound must not cover an ant walking across it.
        var colony = new DesktopColony(800, 500, 1);
        colony.Workers[0].State = AntState.InNest;
        var soilOnly = Render(colony);
        colony.Workers[0].State = AntState.Searching;
        colony.Workers[0].Position = colony.Home + new Vec(12, 5);
        var walkingOnSoil = Render(colony);
        var region = new Int32Rect((int)colony.Home.X, (int)colony.Home.Y - 7, 25, 24);
        var before = new byte[25 * 24 * 4]; var after = new byte[before.Length];
        soilOnly.CopyPixels(region, before, 25 * 4, 0); walkingOnSoil.CopyPixels(region, after, 25 * 4, 0);
        int changed = 0;
        for (int i = 0; i < before.Length; i += 4)
            if (before[i] != after[i] || before[i + 1] != after[i + 1] || before[i + 2] != after[i + 2]) changed++;
        if (changed < 20) throw new InvalidOperationException("Nest soil hides the passing ant.");
    }

    static RenderTargetBitmap Render(DesktopColony colony, OverlaySurface? view = null)
    {
        view ??= new OverlaySurface(colony) { Width = colony.Width, Height = colony.Height };
        view.InvalidateVisual();
        view.Measure(new Size(colony.Width, colony.Height)); view.Arrange(new Rect(0, 0, colony.Width, colony.Height)); view.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)colony.Width, (int)colony.Height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view); return bitmap;
    }
    static bool Visible(BitmapSource bitmap, Vec center)
    {
        var pixels = new byte[28 * 24 * 4]; bitmap.CopyPixels(new Int32Rect((int)center.X - 14, (int)center.Y - 12, 28, 24), pixels, 28 * 4, 0);
        for (int i = 3; i < pixels.Length; i += 4) if (pixels[i] > 0) return true;
        return false;
    }
}
