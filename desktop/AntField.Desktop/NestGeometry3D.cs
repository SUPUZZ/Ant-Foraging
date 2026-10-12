using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using AntField.Core;

namespace AntField.Desktop;

internal static class NestGeometry3D
{
    internal static readonly Point[] Centers = [new(-3.8, 1.8), new(-4.3, -1.8), new(4.3, 1), new(2.9, -2.5)];
    internal static readonly string[] Names = ["蚁后室", "育幼室", "粮仓", "工蚁休息区"];
    static readonly (double X, double Y, double Angle, double Depth)[] Rooms = [(2.05, .96, .22, 1.25), (1.85, 1.1, -.25, 1.05), (1.75, .91, .3, .96), (2.3, .93, -.15, 1.32)];
    readonly record struct Tunnel(Point A, Point B, double Radius, double Recess);
    static readonly Tunnel[] Tunnels = CreateTunnels();
    static readonly Material Soil = Material("#69553b"), Grass = Material("#52623b"), Body = Material("#42271a", true), Dark = Material("#17140f", true), Joint = Material("#65432a", true);
    static readonly MeshGeometry3D SphereMesh = Sphere();
    internal static readonly Model3DGroup[] AntPoses = Enumerable.Range(0, 10).Select(i => Ant(i * Math.PI / 5, false)).ToArray();
    internal static readonly Model3DGroup Queen = Ant(0, true);

    static Tunnel[] CreateTunnels()
    {
        var segments = new System.Collections.Generic.List<Tunnel>();
        void Carve(double width, double seed, params Point[] points)
        {
            // Catmull–Rom splines pass through junctions without straight elbows.
            for (int section = 0; section < points.Length - 1; section++)
            {
                var a = points[Math.Max(0, section - 1)]; var b = points[section];
                var c = points[section + 1]; var d = points[Math.Min(points.Length - 1, section + 2)];
                var last = b;
                for (int step = 1; step <= 12; step++)
                {
                    double t = step / 12.0, t2 = t * t, t3 = t2 * t;
                    static double Blend(double a, double b, double c, double d, double t, double t2, double t3)
                        => .5 * (2 * b + (c - a) * t + (2 * a - 5 * b + 4 * c - d) * t2 + (-a + 3 * b - 3 * c + d) * t3);
                    var p = new Point(Blend(a.X, b.X, c.X, d.X, t, t2, t3), Blend(a.Y, b.Y, c.Y, d.Y, t, t2, t3));
                    double along = section + t;
                    double radius = width * (1 + .18 * Math.Sin(along * 2.7 + seed) + .07 * Math.Sin(along * 6.1 + seed));
                    segments.Add(new Tunnel(last, p, radius, .53 + .08 * Math.Sin(along * 1.7 + seed))); last = p;
                }
            }
        }
        Carve(.3, 0, new(0, 4.55), new(.12, 3.65), new(-.7, 2.95), new(-.35, 2.1), new(.85, 1.25), new(.35, .4), new(-.8, -.5), new(-.15, -1.5), new(1, -2.6), new(.6, -3.6));
        Carve(.25, 1.4, new(-.35, 2.1), new(-1.45, 2.75), new(-2.5, 2.3), Centers[0]);
        Carve(.23, 3.1, new(-.8, -.5), new(-1.7, -.25), new(-2.8, -1.05), Centers[1]);
        Carve(.24, 4.8, new(.85, 1.25), new(1.7, 2), new(2.8, 1.5), Centers[2]);
        Carve(.25, 2.2, new(1, -2.6), new(1.75, -1.85), new(2.55, -2.15), Centers[3]);
        Carve(.16, 5.6, Centers[0], new(-5.25, 2.65), new(-6.15, 2.35));
        Carve(.15, 1.8, Centers[1], new(-5.7, -.9), new(-6.3, -1.45));
        Carve(.17, 3.6, new(2.8, 1.5), new(3.25, 2.45), new(4.35, 2.9));
        return segments.ToArray();
    }

    static double OutlineRadius(int index, double angle)
        => 1 + .11 * Math.Sin(angle * 3 + index * 1.7) + .065 * Math.Cos(angle * 5 - index * .9) + .028 * Math.Sin(angle * 9 + index);

    static double ChamberDistance(int index, double x, double y)
    {
        var c = Centers[index];
        var room = Rooms[index]; double cosine = Math.Cos(room.Angle), sine = Math.Sin(room.Angle);
        double dx = ((x - c.X) * cosine + (y - c.Y) * sine) / room.X;
        double dy = (-(x - c.X) * sine + (y - c.Y) * cosine) / room.Y;
        return Math.Sqrt(dx * dx + dy * dy) / OutlineRadius(index, Math.Atan2(dy, dx));
    }

    internal static bool InChamber(int index, double x, double y) => ChamberDistance(index, x, y) < 1;

    internal static Point ChamberPoint(int index, double x, double y)
    {
        var c = Centers[index]; var room = Rooms[index];
        double u = x * room.X, v = y * room.Y;
        return new Point(c.X + u * Math.Cos(room.Angle) - v * Math.Sin(room.Angle), c.Y + u * Math.Sin(room.Angle) + v * Math.Cos(room.Angle));
    }

    internal static double Depth(double x, double y)
    {
        double depth = 0;
        for (int i = 0; i < Centers.Length; i++)
        {
            double r = ChamberDistance(i, x, y);
            if (r < 1) depth = Math.Max(depth, Rooms[i].Depth * Math.Sqrt(1 - r * r));
        }
        foreach (var tunnel in Tunnels)
        {
            var a = tunnel.A; var b = tunnel.B; double radius = tunnel.Radius;
            if (x < Math.Min(a.X, b.X) - radius || x > Math.Max(a.X, b.X) + radius || y < Math.Min(a.Y, b.Y) - radius || y > Math.Max(a.Y, b.Y) + radius) continue;
            var v = b - a;
            double t = Math.Clamp(Vector.Multiply(new Vector(x - a.X, y - a.Y), v) / v.LengthSquared, 0, 1);
            double d = (new Point(x, y) - (a + v * t)).Length / radius;
            if (d < 1) depth = Math.Max(depth, tunnel.Recess * Math.Sqrt(1 - d * d));
        }
        return -depth;
    }

    internal static GeometryModel3D CreateTerrain()
    {
        const int columns = 200, rows = 108;
        var mesh = new MeshGeometry3D();
        for (int j = 0; j <= rows; j++) for (int i = 0; i <= columns; i++)
        {
            double x = -8 + i * 16.0 / columns, y = -4.2 + j * 8.6 / rows;
            double z = Depth(x, y);
            // Small surface relief keeps both the cut earth and cave walls organic.
            z += (.022 - z * .018) * Math.Sin(x * 17 + y * 9) * Math.Cos(y * 14 - x * 5);
            mesh.Positions.Add(new Point3D(x, y, z));
            mesh.TextureCoordinates.Add(new Point(i / (double)columns, 1 - j / (double)rows));
        }
        for (int j = 0; j < rows; j++) for (int i = 0; i < columns; i++)
        {
            int a = j * (columns + 1) + i, b = a + columns + 1;
            Triangle(mesh, a, a + 1, b); Triangle(mesh, a + 1, b + 1, b);
        }
        Normals(mesh); mesh.Freeze();
        var texture = SoilTexture();
        var brush = new ImageBrush(texture) { ViewportUnits = BrushMappingMode.RelativeToBoundingBox };
        brush.Freeze(); var material = new DiffuseMaterial(brush); material.Freeze();
        return new GeometryModel3D(mesh, material) { BackMaterial = material };
    }

    static BitmapSource SoilTexture()
    {
        const int width = 768, height = 416;
        var pixels = new byte[width * height * 4];
        // Deterministic grain at several scales, with visible sediment layers.
        static double Noise(int x, int y) { uint h = unchecked((uint)(x * 374761393 + y * 668265263)); h = (h ^ (h >> 13)) * 1274126177u; return (h ^ (h >> 16)) / (double)uint.MaxValue; }
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            double wx = -8 + x * 16.0 / width, wy = 4.4 - y * 8.6 / height;
            double depth = -Depth(wx, wy);
            double grain = Noise(x, y), coarse = Noise(x / 5, y / 5);
            double layer = Math.Sin(wy * 3.7 + Math.Sin(wx * .4) * .6);
            double tone = 95 + grain * 30 + coarse * 18 + layer * 7;
            if (grain > .97) tone += 35;
            if (grain < .06) tone -= 28;
            double shade = depth > .02 ? .62 + .19 * (1 - depth / 1.2) : 1;
            int p = (y * width + x) * 4;
            pixels[p] = (byte)Math.Clamp(tone * .53 * shade, 0, 255);
            pixels[p + 1] = (byte)Math.Clamp(tone * .76 * shade, 0, 255);
            pixels[p + 2] = (byte)Math.Clamp(tone * shade, 0, 255); pixels[p + 3] = 255;
        }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze(); return bitmap;
    }

    internal static Model3DGroup CreateSoilBlock()
    {
        var group = new Model3DGroup();
        group.Children.Add(Quad(new(-8, -4.2, 0), new(-8, 4.4, 0), new(-8, 4.4, -2.8), new(-8, -4.2, -2.8), Soil));
        group.Children.Add(Quad(new(8, 4.4, 0), new(8, -4.2, 0), new(8, -4.2, -2.8), new(8, 4.4, -2.8), Soil));
        group.Children.Add(Quad(new(-8, -4.2, 0), new(-8, -4.2, -2.8), new(8, -4.2, -2.8), new(8, -4.2, 0), Soil));
        group.Children.Add(Quad(new(-8, -4.2, -2.8), new(-8, 4.4, -2.8), new(8, 4.4, -2.8), new(8, -4.2, -2.8), Soil));
        group.Freeze(); return group;
    }

    internal static Model3DGroup CreateSurface()
    {
        var group = new Model3DGroup();
        // The surface notch leads into the meandering entrance shaft.
        group.Children.Add(Quad(new(-8, 4.4, 0), new(-8, 4.4, -2.8), new(-.28, 4.4, -2.8), new(-.28, 4.4, 0), Grass));
        group.Children.Add(Quad(new(.28, 4.4, 0), new(.28, 4.4, -2.8), new(8, 4.4, -2.8), new(8, 4.4, 0), Grass));
        group.Children.Add(Quad(new(-.28, 4.4, -.6), new(-.28, 4.4, -2.8), new(.28, 4.4, -2.8), new(.28, 4.4, -.6), Soil));
        var rng = new Random(104);
        for (int i = 0; i < 85; i++)
        {
            double x = -7.8 + rng.NextDouble() * 15.6, z = -.1 - rng.NextDouble() * 2.5;
            if (Math.Abs(x) < .65) continue;
            group.Children.Add(Ellipsoid(new(x, 4.42, z), .06 + rng.NextDouble() * .1, .04, .09, Soil));
            if (i % 3 == 0) group.Children.Add(Rod(new(x, 4.4, z), new(x + .08, 4.65 + rng.NextDouble() * .16, z - .04), .013, Grass));
        }
        group.Freeze(); return group;
    }

    internal static Model3DGroup ChamberRim(int index)
    {
        var group = new Model3DGroup(); var material = Material("#a59e70");
        for (int i = 0; i < 120; i++)
        {
            double a = i * Math.PI / 60, b = (i + 1) * Math.PI / 60;
            var p = ChamberPoint(index, Math.Cos(a) * OutlineRadius(index, a), Math.Sin(a) * OutlineRadius(index, a));
            var q = ChamberPoint(index, Math.Cos(b) * OutlineRadius(index, b), Math.Sin(b) * OutlineRadius(index, b));
            group.Children.Add(Rod(new(p.X, p.Y, .05), new(q.X, q.Y, .05), .009, material));
        }
        group.Freeze(); return group;
    }

    internal static GeometryModel3D Pellet(double x, double y, double z, int seed)
        => Ellipsoid(new(x, y, z), .07 + seed % 3 * .018, .06, .07, Material(seed % 3 == 0 ? "#afae65" : "#c4b17c"));

    internal static Model3DGroup Brood(BroodStage stage, double x, double y, double z)
    {
        var group = new Model3DGroup();
        var material = Material(stage == BroodStage.Pupa ? "#bea46f" : "#e6dcc0", true);
        if (stage == BroodStage.Larva)
        {
            for (int i = 0; i < 5; i++) group.Children.Add(Ellipsoid(new(x + i * .042 - .08, y + Math.Sin(i * .5) * .035, z), .045, .065, .065, material));
        }
        else group.Children.Add(Ellipsoid(new(x, y, z), stage == BroodStage.Egg ? .065 : .14, .07, .07, material));
        group.Freeze(); return group;
    }

    static Model3DGroup Ant(double phase, bool queen)
    {
        var group = new Model3DGroup();
        group.Children.Add(Ellipsoid(new(-.43, 0, .21), queen ? .52 : .36, queen ? .28 : .2, queen ? .25 : .18, Body));
        group.Children.Add(Ellipsoid(new(-.02, 0, .17), .08, .075, .075, Joint));
        group.Children.Add(Ellipsoid(new(.2, 0, .2), .23, .13, .13, Body));
        group.Children.Add(Ellipsoid(new(.57, 0, .21), .19, .16, .14, Body));
        for (int side = -1; side <= 1; side += 2)
        {
            group.Children.Add(Ellipsoid(new(.59, side * .125, .27), .055, .035, .045, Dark));
            var antenna = new Point3D(.87, side * .26, .32);
            group.Children.Add(Rod(new(.67, side * .08, .28), antenna, .017, Joint));
            group.Children.Add(Rod(antenna, new(1.08, side * .38, .3), .013, Joint));
            group.Children.Add(Rod(new(.7, side * .06, .15), new(.82, side * .1, .11), .029, Dark));
            for (int leg = 0; leg < 3; leg++)
            {
                double wave = queen ? 0 : Math.Sin(phase + (leg % 2 == 0 ? 0 : Math.PI) + (side < 0 ? Math.PI : 0));
                double hipX = .3 - leg * .16;
                var hip = new Point3D(hipX, side * .1, .17);
                var knee = new Point3D(hipX + (.22 - leg * .18) + wave * .08, side * .38, .13);
                var foot = new Point3D(hipX + (.4 - leg * .3) + wave * .12, side * .62, .035 + Math.Max(0, wave) * .035);
                group.Children.Add(Rod(hip, knee, .028, Body));
                group.Children.Add(Rod(knee, foot, .02, Joint));
                group.Children.Add(Rod(foot, new(foot.X - .1, foot.Y + side * .055, .015), .013, Dark));
            }
        }
        group.Freeze(); return group;
    }

    static Material Material(string color, bool gloss = false)
    {
        var diffuse = new DiffuseMaterial(AntPainter.ColorBrush(color));
        if (!gloss) { diffuse.Freeze(); return diffuse; }
        var group = new MaterialGroup(); group.Children.Add(diffuse);
        group.Children.Add(new SpecularMaterial(AntPainter.ColorBrush("#655949"), 32)); group.Freeze(); return group;
    }

    static GeometryModel3D Ellipsoid(Point3D center, double x, double y, double z, Material material)
    {
        var transforms = new Transform3DGroup(); transforms.Children.Add(new ScaleTransform3D(x, y, z)); transforms.Children.Add(new TranslateTransform3D(center.X, center.Y, center.Z));
        var model = new GeometryModel3D(SphereMesh, material) { BackMaterial = material, Transform = transforms }; model.Freeze(); return model;
    }

    static MeshGeometry3D Sphere()
    {
        const int slices = 12, stacks = 8;
        var mesh = new MeshGeometry3D();
        for (int j = 0; j <= stacks; j++) for (int i = 0; i <= slices; i++)
        {
            double a = i * Math.PI * 2 / slices, b = j * Math.PI / stacks;
            var v = new Vector3D(Math.Sin(b) * Math.Cos(a), Math.Cos(b), Math.Sin(b) * Math.Sin(a));
            mesh.Positions.Add(new Point3D(v.X, v.Y, v.Z)); mesh.Normals.Add(v);
        }
        for (int j = 0; j < stacks; j++) for (int i = 0; i < slices; i++)
        {
            int a = j * (slices + 1) + i, b = a + slices + 1;
            Triangle(mesh, a, a + 1, b); Triangle(mesh, a + 1, b + 1, b);
        }
        mesh.Freeze(); return mesh;
    }

    static GeometryModel3D Rod(Point3D from, Point3D to, double radius, Material material)
    {
        var axis = to - from; axis.Normalize();
        var u = Vector3D.CrossProduct(axis, Math.Abs(axis.Z) < .9 ? new Vector3D(0, 0, 1) : new Vector3D(0, 1, 0)); u.Normalize();
        var v = Vector3D.CrossProduct(axis, u);
        var mesh = new MeshGeometry3D();
        for (int end = 0; end < 2; end++) for (int i = 0; i < 7; i++)
        {
            double angle = i * Math.PI * 2 / 6;
            var normal = u * Math.Cos(angle) + v * Math.Sin(angle);
            mesh.Positions.Add((end == 0 ? from : to) + normal * radius); mesh.Normals.Add(normal);
        }
        for (int i = 0; i < 6; i++) { Triangle(mesh, i, i + 1, i + 7); Triangle(mesh, i + 1, i + 8, i + 7); }
        mesh.Freeze(); var model = new GeometryModel3D(mesh, material) { BackMaterial = material }; model.Freeze(); return model;
    }

    static GeometryModel3D Quad(Point3D a, Point3D b, Point3D c, Point3D d, Material material)
    {
        var mesh = new MeshGeometry3D { Positions = new Point3DCollection { a, b, c, d }, TriangleIndices = new Int32Collection { 0, 1, 2, 0, 2, 3 } };
        Normals(mesh); mesh.Freeze(); var model = new GeometryModel3D(mesh, material) { BackMaterial = material }; model.Freeze(); return model;
    }

    static void Triangle(MeshGeometry3D mesh, int a, int b, int c) { mesh.TriangleIndices.Add(a); mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(c); }
    static void Normals(MeshGeometry3D mesh)
    {
        var normals = new Vector3D[mesh.Positions.Count];
        for (int i = 0; i < mesh.TriangleIndices.Count; i += 3)
        {
            int a = mesh.TriangleIndices[i], b = mesh.TriangleIndices[i + 1], c = mesh.TriangleIndices[i + 2];
            var n = Vector3D.CrossProduct(mesh.Positions[b] - mesh.Positions[a], mesh.Positions[c] - mesh.Positions[a]);
            normals[a] += n; normals[b] += n; normals[c] += n;
        }
        foreach (var value in normals) { var n = value; if (n.LengthSquared > 0) n.Normalize(); mesh.Normals.Add(n); }
    }
}
