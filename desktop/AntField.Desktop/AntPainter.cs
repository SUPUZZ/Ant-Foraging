using System;
using System.Globalization;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AntField.Desktop;

internal static class AntPainter
{
    static readonly Brush Shadow = Frozen(new SolidColorBrush(Color.FromArgb(42, 0, 0, 0)));
    internal static T Frozen<T>(T value) where T : Freezable { value.Freeze(); return value; }
    static readonly Dictionary<string, Brush> Colors = new();
    static readonly ImageSource[] Poses = LoadPoses();
    static ImageSource[] LoadPoses()
    {
        var atlas = new BitmapImage(new Uri("pack://application:,,,/AntField;component/Assets/ant-atlas.png", UriKind.Absolute));
        atlas.Freeze(); var poses = new ImageSource[50];
        for (int row = 0; row < 2; row++) for (int frame = 0; frame < 25; frame++)
        {
            var pose = new CroppedBitmap(atlas, new Int32Rect(frame * 192, row * 128, 192, 128)); pose.Freeze(); poses[row * 25 + frame] = pose;
        }
        return poses;
    }
    internal static Brush ColorBrush(string color)
    {
        if (!Colors.TryGetValue(color, out var brush)) Colors[color] = brush = Frozen(new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)));
        return brush;
    }
    internal static void Text(DrawingContext dc, string text, Point position, double size, Brush brush, double dpi = 1)
        => dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), size, brush, dpi), position);
    internal static void Draw(DrawingContext dc, Point position, double heading, double size, double distance, double time, bool food, bool moving = true, double elevation = 0)
    {
        dc.PushTransform(new TranslateTransform(position.X, position.Y));
        dc.PushTransform(new RotateTransform(heading * 180 / Math.PI)); dc.PushTransform(new ScaleTransform(size, size));
        dc.DrawEllipse(Shadow, null, new Point(1 + elevation * 2, 1.3 + elevation * 3), 8.5, 2.8);
        int poseIndex = moving ? (int)((distance * .5 / (Math.PI * 2) % 1) * 24) : 24;
        dc.DrawImage(Poses[(food ? 25 : 0) + poseIndex], new Rect(-12, -8, 24, 16));
        dc.Pop(); dc.Pop(); dc.Pop();
    }
}
