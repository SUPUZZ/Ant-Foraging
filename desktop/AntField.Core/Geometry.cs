using System.Text.Json.Serialization;

namespace AntField.Core;

public readonly record struct Vec(double X, double Y)
{
    public override string ToString() => $"({X:F2}, {Y:F2})";
    [JsonIgnore] public double Length => Math.Sqrt(X * X + Y * Y);
    [JsonIgnore] public double Angle => Math.Atan2(Y, X);
    [JsonIgnore] public Vec Normalized => Length > .00001 ? this / Length : new(0, 0);
    public static Vec operator +(Vec a, Vec b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec operator -(Vec a, Vec b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec operator *(Vec a, double b) => new(a.X * b, a.Y * b);
    public static Vec operator /(Vec a, double b) => new(a.X / b, a.Y / b);
    public static Vec Direction(double angle) => new(Math.Cos(angle), Math.Sin(angle));
}
public readonly record struct Wall(long Id, double X, double Y, double Width, double Height)
{
    public (double Start, double End)? Segment(Vec a, Vec b, double margin = 0)
    {
        double start = 0, end = 1;
        var d = b - a;
        bool Axis(double origin, double delta, double min, double max)
        {
            if (Math.Abs(delta) < 1e-9) return origin >= min && origin <= max;
            double t0 = (min - origin) / delta, t1 = (max - origin) / delta;
            start = Math.Max(start, Math.Min(t0, t1)); end = Math.Min(end, Math.Max(t0, t1));
            return start <= end;
        }
        return Axis(a.X, d.X, X - margin, X + Width + margin) && Axis(a.Y, d.Y, Y - margin, Y + Height + margin) ? (start, end) : null;
    }
    public bool Contains(Vec p, double margin = 0) => p.X >= X - margin && p.X <= X + Width + margin && p.Y >= Y - margin && p.Y <= Y + Height + margin;
}
public enum AntState { InNest, Searching, Returning, Climbing, OnWindow, Descending, UnderWindow }
public enum BroodStage { Egg, Larva, Pupa }
public sealed class Worker
{
    public int Id { get; init; }
    public Vec Position { get; set; }
    public double Heading { get; set; }
    public double Distance { get; set; }
    public AntState State { get; set; } = AntState.InNest;
    public bool HasFood { get; set; }
    public double Energy { get; set; } = 100;
    public double Timer { get; set; }
    public double Decision { get; set; }
    public double Turn { get; set; }
    public double AvoidUntil { get; set; }
    public double ClimbCooldown { get; set; }
    public Vec LastFloor { get; set; }
    public Wall? ClimbWall { get; set; }
    public double Elevation { get; set; }
    public Vec? EscapeTarget { get; set; }
    public Vec? ReturnTarget { get; set; }
    public Vec? ProgressOrigin { get; set; }
    public double ProgressTime { get; set; }
    public Vec? DetourTarget { get; set; }
    public double DetourUntil { get; set; }
    public int? FoodSourceId { get; set; }
}
public sealed class FoodPatch
{
    public int Id { get; init; }
    public Vec Position { get; set; }
    public int Amount { get; set; }
    public int Capacity { get; init; }
    [JsonIgnore] public bool IsLarge => Capacity >= 160;
    [JsonIgnore] public double Radius => IsLarge ? 26 * (.45 + .55 * Math.Sqrt(Math.Clamp(Amount / (double)Math.Max(1, Capacity), 0, 1))) : 10;
}
public sealed class Brood
{
    public BroodStage Stage { get; set; }
    public double Age { get; set; }
}
