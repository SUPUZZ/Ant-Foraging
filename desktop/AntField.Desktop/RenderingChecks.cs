using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AntField.Core;

namespace AntField.Desktop;

internal static class RenderingChecks
{
    internal static bool WindowOcclusion()
    {
        var colony = new DesktopColony(800, 500, 3);
        colony.Workers[0].Position = new Vec(300, 220);
        colony.Workers[1].Position = new Vec(150, 150);
        colony.Workers[2].Position = new Vec(410, 290);
        foreach (var ant in colony.Workers) ant.State = AntState.Searching;
        colony.UpdateWalls([new Wall(1, 250, 150, 210, 190), new Wall(2, 385, 260, 180, 130)]);
        var ground = Render(colony);
        if (Visible(ground, colony.Workers[0].Position) || Visible(ground, colony.Workers[2].Position) || !Visible(ground, colony.Workers[1].Position))
            throw new InvalidOperationException("窗口下方或重叠窗口的遮挡绘制不正确。");
        foreach (var state in new[] { AntState.Climbing, AntState.OnWindow, AntState.Descending })
        {
            colony.Workers[0].State = state;
            if (Visible(Render(colony), colony.Workers[0].Position)) throw new InvalidOperationException("旧爬墙状态的工蚁显示在窗口表面。");
        }
        var position = colony.Workers[0].Position;
        colony.UpdateWalls([new Wall(1, 280, 180, 210, 190)]);
        if (colony.Workers[0].Position != position || Visible(Render(colony), position)) throw new InvalidOperationException("移动窗口后工蚁移位或显示在窗口上。");
        colony.Workers[0].State = AntState.UnderWindow;
        colony.UpdateWalls([]);
        if (!Visible(Render(colony), colony.Workers[0].Position)) throw new InvalidOperationException("移走窗口后未显露原位置的工蚁。");
        return true;
    }
    static RenderTargetBitmap Render(DesktopColony colony)
    {
        var view = new OverlaySurface(colony) { Width = colony.Width, Height = colony.Height };
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
