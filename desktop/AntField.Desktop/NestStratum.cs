using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media.Media3D;

namespace AntField.Desktop;

// Only the selected stratum is meshed. Its shape is stable across restarts,
// independent of the number of older strata or subsequent excavation.
internal sealed class NestStratum
{
    readonly List<(Point A, Point B, double Width)> passages = new();
    readonly double[] radiusX = new double[4], radiusY = new double[4], angles = new double[4];
    internal readonly Point[] Centers = new Point[4];
    internal double Excavated { get; }
    internal NestStratum(long layer, double excavated)
    {
        Excavated = Math.Clamp(excavated, 0, 4);
        var random = new Random(unchecked((int)(layer ^ (layer >> 32)) * 7919 + 173));
        Point[] positions = [new(-3.7, 2.1), new(3.8, .6), new(-3.8, -1.6), new(3.4, -2.6)];
        for (int i = 0; i < 4; i++)
        {
            Centers[i] = positions[i] + new Vector((random.NextDouble() - .5) * .7, (random.NextDouble() - .5) * .35);
            radiusX[i] = 1.5 + random.NextDouble() * .4; radiusY[i] = .7 + random.NextDouble() * .25;
            angles[i] = (random.NextDouble() - .5) * .6;
        }
        Point[] shaft = [new(0, 4.55), new(-.4, 3.5), new(.3, 2.1), new(-.7, .6), new(.5, -1.6), new(-.3, -2.7), new(0, -4.3)];
        int last = Math.Min(6, (int)Math.Ceiling(Excavated) + 1);
        if (Excavated >= 3.95) last = 6;
        Curve(shaft[..(last + 1)], .24, 1);
        for (int i = 0; i < 4; i++)
        {
            double fraction = Math.Clamp(Excavated - i, 0, 1);
            if (fraction <= 0) continue;
            var start = shaft[Math.Min(5, i + 2)]; var end = Centers[i];
            double direction = end.X < 0 ? -1 : 1;
            Point[] branch = [start, new(start.X + direction * .9, start.Y + .6), new(end.X - direction * .8, end.Y - .3), end];
            Curve(branch, .21, Math.Min(1, fraction / .65));
            // Older rooms sprout winding bypasses as excavation progresses.
            double bypass = Math.Clamp((Excavated - i - 1) / 3, 0, 1);
            if (bypass > 0) Curve([end, new(end.X + direction * 1.3, end.Y + .5), new(end.X + direction * 1.5, end.Y - .5)], .12, bypass);
        }
    }

    void Curve(Point[] points, double width, double fraction)
    {
        int steps = (int)Math.Ceiling((points.Length - 1) * 20 * fraction);
        var previous = points[0];
        for (int n = 1; n <= steps; n++)
        {
            double along = Math.Min((points.Length - 1) * fraction, n / 20.0);
            int section = Math.Min(points.Length - 2, (int)along); double t = along - section;
            var a = points[Math.Max(0, section - 1)]; var b = points[section]; var c = points[section + 1]; var d = points[Math.Min(points.Length - 1, section + 2)];
            static double Blend(double a, double b, double c, double d, double t) => .5 * (2 * b + (c - a) * t + (2 * a - 5 * b + 4 * c - d) * t * t + (-a + 3 * b - 3 * c + d) * t * t * t);
            var point = new Point(Blend(a.X, b.X, c.X, d.X, t), Blend(a.Y, b.Y, c.Y, d.Y, t));
            passages.Add((previous, point, width * (1 + .14 * Math.Sin(n * .37)))); previous = point;
        }
    }

    internal double Depth(double x, double y)
    {
        double depth = 0;
        foreach (var (a, b, width) in passages)
        {
            if (x < Math.Min(a.X, b.X) - width || x > Math.Max(a.X, b.X) + width || y < Math.Min(a.Y, b.Y) - width || y > Math.Max(a.Y, b.Y) + width) continue;
            var v = b - a; double t = v.LengthSquared == 0 ? 0 : Math.Clamp(Vector.Multiply(new Vector(x - a.X, y - a.Y), v) / v.LengthSquared, 0, 1);
            double r = (new Point(x, y) - (a + v * t)).Length / width;
            if (r < 1) depth = Math.Max(depth, .6 * Math.Sqrt(1 - r * r));
        }
        for (int i = 0; i < 4; i++)
        {
            double progress = Math.Clamp(Excavated - i, 0, 1);
            // Excavation first reaches the room before hollowing it out.
            if (progress < .65) continue;
            double r = ChamberDistance(i, x, y);
            if (r < 1) depth = Math.Max(depth, 1.15 * Math.Sqrt(1 - r * r));
        }
        return -depth;
    }
    double ChamberDistance(int i, double x, double y)
    {
        double progress = Math.Clamp(Excavated - i, 0, 1);
        if (progress < .65) return double.PositiveInfinity;
        var delta = new Point(x, y) - Centers[i]; double c = Math.Cos(angles[i]), s = Math.Sin(angles[i]);
        double u = (delta.X * c + delta.Y * s) / radiusX[i], v = (-delta.X * s + delta.Y * c) / radiusY[i];
        double angle = Math.Atan2(v, u);
        return Math.Sqrt(u * u + v * v) / ((.15 + .85 * (progress - .65) / .35) * (1 + .12 * Math.Sin(angle * 3 + i) + .06 * Math.Cos(angle * 7)));
    }
    internal bool InChamber(int i, double x, double y) => i < Math.Floor(Excavated) && ChamberDistance(i, x, y) < 1;
    internal GeometryModel3D Rim(int i)
    {
        var mesh = new MeshGeometry3D();
        for (int n = 0; n <= 120; n++)
        {
            double angle = n * Math.PI / 60;
            double radius = 1 + .12 * Math.Sin(angle * 3 + i) + .06 * Math.Cos(angle * 7);
            double u = Math.Cos(angle) * radiusX[i] * radius, v = Math.Sin(angle) * radiusY[i] * radius;
            double x = u * Math.Cos(angles[i]) - v * Math.Sin(angles[i]), y = u * Math.Sin(angles[i]) + v * Math.Cos(angles[i]);
            foreach (double edge in new[] { .995, 1.005 })
            {
                mesh.Positions.Add(new Point3D(Centers[i].X + x * edge, Centers[i].Y + y * edge, .045));
                mesh.Normals.Add(new Vector3D(0, 0, 1));
            }
            if (n == 0) continue;
            int k = n * 2;
            foreach (int index in new[] { k - 2, k - 1, k, k, k - 1, k + 1 }) mesh.TriangleIndices.Add(index);
        }
        mesh.Freeze(); var material = new DiffuseMaterial(AntPainter.ColorBrush("#a59e70")); material.Freeze();
        var model = new GeometryModel3D(mesh, material) { BackMaterial = material }; model.Freeze(); return model;
    }

}
