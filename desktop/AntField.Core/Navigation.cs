namespace AntField.Core;

public sealed class Navigation
{
    public const double Cell = 18;
    Func<Vec, bool> free = _ => true;
    Func<Vec, Vec, bool>? clear;
    public bool Clear(Vec a, Vec b)
    {
        if (clear != null) return clear(a, b);
        int steps = Math.Max(1, (int)Math.Ceiling((b - a).Length / 3));
        for (int i = 0; i <= steps; i++) if (!free(a + (b - a) * (i / (double)steps))) return false;
        return true;
    }
    public int Columns { get; private set; }
    public int Rows { get; private set; }
    public int[] HomeDistance { get; private set; } = [];
    public bool[] Floor { get; private set; } = [];
    public int Index(Vec p)
    {
        int x = (int)Math.Floor(p.X / Cell), y = (int)Math.Floor(p.Y / Cell);
        return x < 0 || y < 0 || x >= Columns || y >= Rows ? -1 : y * Columns + x;
    }
    public Vec Center(int index) => new((index % Columns + .5) * Cell, (index / Columns + .5) * Cell);
    public bool Reachable(Vec p)
    {
        int i = Index(p);
        return i >= 0 && (HomeDistance[i] >= 0 || Anchor(p) >= 0);
    }
    int Anchor(Vec p)
    {
        int current = Index(p);
        if (current < 0) return -1;
        if (HomeDistance[current] >= 0 && Clear(p, Center(current))) return current;
        int px = current % Columns, py = current / Columns, best = -1; double distance = double.MaxValue;
        // Cell centers are only routing samples. A free point beside a window
        // corner can connect to an adjacent sample even if its own center is blocked.
        for (int y = Math.Max(0, py - 2); y <= Math.Min(Rows - 1, py + 2); y++)
        for (int x = Math.Max(0, px - 2); x <= Math.Min(Columns - 1, px + 2); x++)
        {
            int index = y * Columns + x;
            if (HomeDistance[index] < 0) continue;
            double d = (Center(index) - p).Length;
            if (d < distance && Clear(p, Center(index))) { best = index; distance = d; }
        }
        return best;
    }
    public void Rebuild(double width, double height, Vec home, Func<Vec, bool> free, Func<Vec, Vec, bool>? clear = null)
    {
        this.free = free; this.clear = clear;
        Columns = Math.Max(1, (int)Math.Ceiling(width / Cell)); Rows = Math.Max(1, (int)Math.Ceiling(height / Cell));
        Floor = new bool[Columns * Rows]; HomeDistance = Enumerable.Repeat(-1, Floor.Length).ToArray();
        for (int i = 0; i < Floor.Length; i++) Floor[i] = free(Center(i));
        int start = Index(home);
        if (start < 0) return;
        Floor[start] = true; HomeDistance[start] = 0;
        var queue = new Queue<int>(); queue.Enqueue(start);
        while (queue.TryDequeue(out int index)) foreach (int next in Neighbors(index))
        {
            if (!Floor[next] || HomeDistance[next] >= 0 || !Clear(Center(index), Center(next))) continue;
            HomeDistance[next] = HomeDistance[index] + 1; queue.Enqueue(next);
        }
    }
    public IEnumerable<int> Neighbors(int index)
    {
        int x = index % Columns, y = index / Columns;
        if (x > 0) yield return index - 1;
        if (x < Columns - 1) yield return index + 1;
        if (y > 0) yield return index - Columns;
        if (y < Rows - 1) yield return index + Columns;
    }
    public Vec HomeTarget(Vec p, Vec home)
    {
        int i = Anchor(p);
        if (Clear(p, home)) return home;
        if (i < 0 || HomeDistance[i] < 0) return Nearest(p, free) ?? p;
        int best = i;
        foreach (int next in Neighbors(i))
            if (HomeDistance[next] >= 0 && HomeDistance[next] < HomeDistance[best] && Clear(p, Center(next))) best = next;
        return Center(best);
    }
    public Vec? Detour(Vec p, double heading, int id)
    {
        Vec? best = null; double bestScore = double.MinValue;
        int left = Math.Max(0, (int)((p.X - 95) / Cell)), right = Math.Min(Columns - 1, (int)((p.X + 95) / Cell));
        int top = Math.Max(0, (int)((p.Y - 95) / Cell)), bottom = Math.Min(Rows - 1, (int)((p.Y + 95) / Cell));
        for (int y = top; y <= bottom; y++) for (int x = left; x <= right; x++)
        {
            int index = y * Columns + x;
            if (HomeDistance[index] < 0) continue;
            var target = Center(index); var delta = target - p;
            if (delta.Length < 55 || delta.Length > 95 || !Clear(p, target)) continue;
            // Prefer open space; a small individual bias prevents the whole colony
            // from choosing the same wall-following direction at a shared corner.
            int exits = Neighbors(index).Count(next => Floor[next]);
            double score = delta.Length + exits * 9 + Math.Cos(delta.Angle - heading + (id % 2 == 0 ? .7 : -.7)) * 12;
            if (score > bestScore) { best = target; bestScore = score; }
        }
        return best;
    }

    public Vec? Nearest(Vec p, Func<Vec, bool> free)
    {
        double bestDistance = double.MaxValue; Vec? best = null;
        for (int i = 0; i < Floor.Length; i++) if (HomeDistance[i] >= 0)
        {
            var point = Center(i); double d = (point - p).Length;
            if (d < bestDistance && free(point)) { bestDistance = d; best = point; }
        }
        return best;
    }
}
