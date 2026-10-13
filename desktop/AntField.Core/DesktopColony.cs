namespace AntField.Core;

public sealed class DesktopColony
{
    readonly Random random;
    double nextSpawn = 180, eggClock, metabolism;
    int foodId, workerId;
    public double Width { get; private set; }
    public double Height { get; private set; }
    Vec? home;
    public Vec Home => home ?? new(Width / 2, Height - 24);
    public IReadOnlyList<Wall> Screens { get; private set; } = [];
    public IReadOnlyList<Wall> FoodAreas { get; set; } = [];
    public bool OnScreen(Vec p) => Screens.Count == 0 ? p.X >= 0 && p.X < Width && p.Y >= 0 && p.Y < Height : Screens.Any(s => s.Contains(p));
    public void ConfigureScreens(double width, double height, IEnumerable<Wall> screens, Vec nest, Vec shift = default)
    {
        foreach (var ant in Workers) { ant.Position += shift; ant.LastFloor += shift; ant.EscapeTarget = null; ant.ReturnTarget = null; }
        foreach (var food in Foods) food.Position += shift;
        Walls = Walls.Select(w => w with { X = w.X + shift.X, Y = w.Y + shift.Y }).ToArray();
        Width = Math.Max(200, width); Height = Math.Max(160, height); Screens = screens.ToArray(); home = nest;
        foreach (var ant in Workers.Where(a => !OnScreen(a.Position)))
        {
            var candidates = Screens.Select(s => new Vec(Math.Clamp(ant.Position.X, s.X + 8, s.X + s.Width - 8), Math.Clamp(ant.Position.Y, s.Y + 8, s.Y + s.Height - 8)));
            ant.Position = candidates.OrderBy(p => (p - ant.Position).Length).FirstOrDefault(Home);
            ant.State = AntState.UnderWindow; ant.ClimbWall = null; ant.Elevation = 0;
        }
        Rebuild(); ResolveChanges();
    }
    public List<Worker> Workers { get; } = [];
    public List<FoodPatch> Foods { get; } = [];
    public List<Brood> Nursery { get; } = [];
    public IReadOnlyList<Wall> Walls { get; private set; } = [];
    public Navigation Navigation { get; } = new();
    public float[] Pheromones { get; private set; } = [];
    public double Time { get; private set; }
    public double Decay { get; set; } = .06;
    // Retained for compatibility with older saves; workers now stay on the ground.
    public double ClimbProbability { get => 0; set { } }
    public double SpawnInterval { get; set; } = 180;
    public NestDevelopment Development { get; private set; } = new();
    public int Delivered { get; private set; }
    public int Stored { get; private set; } = 12;
    public int Consumed { get; private set; }
    public int Spawned { get; private set; }
    public int Expired { get; private set; }
    public int Births { get; private set; }
    public bool FoodSpaceAvailable { get; private set; } = true;
    public int Carrying => Workers.Count(a => a.HasFood);
    public int Remaining => Foods.Sum(f => f.Amount);
    public ColonySnapshot Snapshot() => new(3, Width, Height, Time, Delivered, Stored, Consumed, Spawned, Expired, Births, ClimbProbability, SpawnInterval,
        Workers.Select(a => new WorkerSnapshot(a.Id, a.Position.X, a.Position.Y, a.Heading, a.Distance, a.Energy, a.HasFood, a.State == AntState.InNest, a.Timer, a.FoodSourceId, a.State)).ToArray(),
        Foods.Select(f => new FoodSnapshot(f.Id, f.Position.X, f.Position.Y, f.Amount, f.Capacity)).ToArray(), Nursery.Select(b => new BroodSnapshot(b.Stage, b.Age)).ToArray(), Screens.ToArray(), Home, (float[])Pheromones.Clone(), nextSpawn, eggClock, metabolism, Development.WorkHours);
    public static DesktopColony Restore(ColonySnapshot saved, double width, double height)
    {
        if (saved.Version is not (1 or 2 or 3) || !double.IsFinite(saved.Width + saved.Height + saved.Time + saved.NextSpawn + saved.EggClock + saved.Metabolism + saved.SpawnInterval) || saved.Width < 200 || saved.Height < 160 || saved.Width > 50000 || saved.Height > 50000 || saved.Workers is null || saved.Foods is null || saved.Nursery is null || saved.Workers.Length > 120 || saved.Foods.Length > 9 || saved.Nursery.Length > 18 || saved.Time < 0) throw new ArgumentException("无效的蚁群存档。");
        var colony = new DesktopColony(width, height, 0) {
            Time = saved.Time, Delivered = Math.Max(0, saved.Delivered), Stored = Math.Max(0, saved.Stored), Consumed = Math.Max(0, saved.Consumed), Spawned = Math.Max(0, saved.Spawned), Expired = Math.Max(0, saved.Expired), Births = Math.Max(0, saved.Births),
            ClimbProbability = Math.Clamp(saved.ClimbProbability, 0, .05), SpawnInterval = saved.Version < 3 ? Math.Max(180, saved.SpawnInterval) : Math.Clamp(saved.SpawnInterval, 120, 600),
            Development = new NestDevelopment(saved.NestWorkHours)
        };
        colony.nextSpawn = saved.Version < 3 ? Math.Max(120, saved.NextSpawn) : Math.Max(0, saved.NextSpawn); colony.eggClock = Math.Max(0, saved.EggClock); colony.metabolism = Math.Clamp(saved.Metabolism, 0, 25);
        foreach (var ant in saved.Workers)
        {
            if (!double.IsFinite(ant.X + ant.Y + ant.Heading + ant.Energy + ant.Distance + ant.Timer)) throw new ArgumentException("无效的工蚁位置。");
            var state = ant.InNest ? AntState.InNest : ant.State is AntState.Returning || ant.HasFood ? AntState.Returning : AntState.Searching;
            colony.Workers.Add(new Worker { Id = ant.Id, Position = new(ant.X * width / saved.Width, ant.Y * height / saved.Height), Heading = ant.Heading, Distance = Math.Max(0, ant.Distance), Energy = Math.Clamp(ant.Energy, 0, 100), HasFood = ant.HasFood, State = state, Timer = ant.InNest ? Math.Max(0, ant.Timer) : 0, FoodSourceId = ant.FoodSourceId });
        }
        foreach (var f in saved.Foods) if (f.Amount > 0) colony.Foods.Add(new FoodPatch { Id = f.Id, Position = new(f.X * width / saved.Width, f.Y * height / saved.Height), Amount = f.Amount, Capacity = Math.Max(f.Amount, f.Capacity) });
        colony.Nursery.Clear();
        foreach (var brood in saved.Nursery) colony.Nursery.Add(new Brood { Stage = brood.Stage, Age = Math.Max(0, brood.Age) });
        colony.workerId = colony.Workers.Count == 0 ? 0 : colony.Workers.Max(a => a.Id) + 1;
        colony.foodId = colony.Foods.Count == 0 ? 0 : colony.Foods.Max(f => f.Id) + 1;
        if (saved.Screens is { Length: > 0 } screens && saved.Home is Vec nest)
            colony.ConfigureScreens(width, height, screens.Select(s => new Wall(s.Id, s.X * width / saved.Width, s.Y * height / saved.Height, s.Width * width / saved.Width, s.Height * height / saved.Height)), new Vec(nest.X * width / saved.Width, nest.Y * height / saved.Height));
        if (saved.Pheromones is { } trail && width == saved.Width && height == saved.Height && trail.Length == colony.Pheromones.Length)
        {
            if (trail.Any(v => !float.IsFinite(v) || v < 0)) throw new ArgumentException("无效的气味路径。");
            for (int i = 0; i < trail.Length; i++) if (colony.Navigation.Floor[i]) colony.Pheromones[i] = Math.Min(24, trail[i]);
        }
        colony.ResolveChanges(); return colony;
    }
    public DesktopColony(double width, double height, int count = 48, int seed = 42)
    {
        random = new Random(seed); Width = Math.Max(200, width); Height = Math.Max(160, height);
        Rebuild();
        for (int i = 0; i < count; i++) AddWorker();
        for (int i = 0; i < 5; i++) Nursery.Add(new Brood { Stage = (BroodStage)(i % 3), Age = i * 8 });
    }
    void AddWorker() => Workers.Add(new Worker { Id = workerId++, Position = Home, Heading = -Math.PI * random.NextDouble(), Timer = 1 + random.NextDouble() * 8 });
    public bool IsNestApron(Vec p) => Math.Abs(p.X - Home.X) < 48 && p.Y > Home.Y - 24 && p.Y < Home.Y + 24;
    public bool Free(Vec p) => p.X >= 7 && p.X < Width - 7 && p.Y >= 7 && p.Y < Height - 7 && OnScreen(p) && (IsNestApron(p) || !Walls.Any(w => w.Contains(p, 7)));
    // Physical floor access and a route home are independent. A closed nest
    // must not classify every worker outside it as being under a window.
    public bool CanWalk(Vec p) => Free(p);
    public bool NestExitOpen { get; private set; } = true;
    bool VisibleFloor(Vec p) => FoodAreas.Count == 0 || FoodAreas.Any(s => s.Contains(p, -7));
    bool FoodSpace(Vec p, double margin = 14) => (FoodAreas.Count == 0 || FoodAreas.Any(s => s.Contains(p, -margin))) && CanWalk(p) && (!NestExitOpen || Navigation.Reachable(p)) && (p - Home).Length > 100 && !Walls.Any(w => w.Contains(p, margin)) && p.X >= margin && p.Y >= margin && p.X < Width - margin && p.Y < Height - margin;
    bool GroundClear(Vec a, Vec b)
    {
        if (!Free(a) || !Free(b)) return false;
        if (Screens.Count > 0)
        {
            double end = 0;
            foreach (var interval in Screens.Select(s => s.Segment(a, b)).Where(t => t != null).Select(t => t!.Value).OrderBy(t => t.Start))
            {
                if (interval.Start > end + 1e-8) return false;
                end = Math.Max(end, interval.End);
            }
            if (end < 1 - 1e-8) return false;
        }
        foreach (var wall in Walls)
        {
            if (wall.Segment(a, b, 7) is not { } range) continue;
            if (!IsNestApron(a + (b - a) * range.Start) || !IsNestApron(a + (b - a) * range.End)) return false;
        }
        return true;
    }
    void Rebuild()
    {
        Navigation.Rebuild(Width, Height, Home, Free, GroundClear);
        // Probe a real exit onto visible desktop, not a path through the hidden
        // taskbar strip below a window. The reserved nest apron is not an exit.
        NestExitOpen = Enumerable.Range(0, 32).Any(i => {
            var exit = Home + Vec.Direction(i * Math.PI / 16) * 60;
            return !IsNestApron(exit) && VisibleFloor(exit) && Free(exit) && GroundClear(Home, exit);
        });
        if (Pheromones.Length != Navigation.Floor.Length) Pheromones = new float[Navigation.Floor.Length];
        for (int i = 0; i < Pheromones.Length; i++) if (!Navigation.Floor[i]) Pheromones[i] = 0;
    }
    public void Resize(double width, double height)
    {
        Width = Math.Max(200, width); Height = Math.Max(160, height); Rebuild(); ResolveChanges();
    }
    public void UpdateWalls(IEnumerable<Wall> walls)
    {
        var updated = walls.OrderBy(w => w.Id).ToArray();
        if (Walls.SequenceEqual(updated)) return;
        Walls = updated; Rebuild(); ResolveChanges();
    }
    void ResolveChanges()
    {
        foreach (var food in Foods.Where(f => !FoodSpace(f.Position, f.IsLarge ? 30 : 14))) { Expired += food.Amount; food.Amount = 0; }
        Foods.RemoveAll(f => f.Amount <= 0);
        foreach (var ant in Workers)
        {
            if (ant.State == AntState.InNest) { ant.Position = Home; continue; }
            bool climbing = ant.State is AntState.Climbing or AntState.OnWindow or AntState.Descending;
            if (climbing || !CanWalk(ant.Position))
            {
                // Window movement changes occlusion, never the worker's ground position.
                ant.Position = new Vec(Math.Clamp(ant.Position.X, 7, Width - 7), Math.Clamp(ant.Position.Y, 7, Height - 7));
                ant.State = CanWalk(ant.Position) ? ant.HasFood ? AntState.Returning : AntState.Searching : AntState.UnderWindow;
                ant.Elevation = 0; ant.ClimbWall = null;
            }
            else if (ant.State == AntState.UnderWindow) ant.State = ant.HasFood || ant.Energy < 24 ? AntState.Returning : AntState.Searching;
            ant.EscapeTarget = null; ant.ReturnTarget = null; ant.DetourTarget = null; ant.ProgressOrigin = null; ant.Decision = 0;
        }
    }
    public bool SpawnFood()
    {
        if (!NestExitOpen) { FoodSpaceAvailable = false; return false; }
        if (Foods.Count >= 9) return false;
        bool large = !Foods.Any(f => f.IsLarge) && random.NextDouble() < .2;
        var candidates = Enumerable.Range(0, Navigation.Floor.Length).Where(i => FoodSpace(Navigation.Center(i), large ? 30 : 14)).ToArray();
        // A narrow desktop strip may fit crumbs but not a large morsel.
        if (large && candidates.Length == 0) { large = false; candidates = Enumerable.Range(0, Navigation.Floor.Length).Where(i => FoodSpace(Navigation.Center(i))).ToArray(); }
        FoodSpaceAvailable = candidates.Length > 0;
        if (!FoodSpaceAvailable) return false;
        for (int attempt = 0; attempt < 80; attempt++)
        {
            var point = Navigation.Center(candidates[random.Next(candidates.Length)]);
            if (Foods.Any(f => (f.Position - point).Length < 65)) continue;
            int amount = large ? 160 + random.Next(121) : 8 + random.Next(12);
            Foods.Add(new FoodPatch { Id = foodId++, Position = point, Amount = amount, Capacity = amount }); Spawned += amount; return true;
        }
        return false;
    }
    public void Step(double dt)
    {
        if (!double.IsFinite(dt) || dt <= 0) return;
        int steps = (int)Math.Ceiling(dt / (1.0 / 30)); double h = dt / steps;
        for (int i = 0; i < steps; i++) Tick(h);
    }
    double Scent(Vec p) { int i = Navigation.Index(p); return i >= 0 && CanWalk(p) ? Pheromones[i] : 0; }
    Vec ReturnDirection(Worker ant)
    {
        if (!NestExitOpen || !Navigation.Reachable(ant.Position))
        {
            // Keep the carried food and wait/move locally on this floor.
            // Never escape through a window toward a remote home component.
            if (ant.ReturnTarget is Vec waiting && (waiting - ant.Position).Length > 3 && CanWalk(waiting) && VisibleFloor(waiting) && !IsNestApron(waiting) && Navigation.Clear(ant.Position, waiting)) return waiting;
            ant.ReturnTarget = Navigation.Detour(ant.Position, ant.Heading, ant.Id, p => VisibleFloor(p) && !IsNestApron(p)) ?? ant.Position;
            return ant.ReturnTarget.Value;
        }
        // Keep a local waypoint until reached; selecting afresh every frame causes zigzags.
        if (ant.ReturnTarget is Vec previous && (previous - ant.Position).Length > 3 && CanWalk(previous) && Navigation.Clear(ant.Position, previous)) return previous;
        int current = Navigation.Index(ant.Position);
        var fallback = Navigation.HomeTarget(ant.Position, Home);
        var delta = fallback - ant.Position;
        if (delta.Length > 30)
        {
            var local = ant.Position + delta.Normalized * 30;
            if (CanWalk(local) && Navigation.Clear(ant.Position, local)) fallback = local;
        }
        Vec best = fallback; double bestScore = 0;
        if (current >= 0 && Navigation.HomeDistance[current] >= 0 && (ant.Position - Home).Length > 45)
        {
            double direction = (fallback - ant.Position).Angle;
            for (int k = 0; k < 16; k++)
            {
                var candidate = ant.Position + Vec.Direction(direction + k * Math.PI / 8) * 30;
                int cell = Navigation.Index(candidate);
                // Food scent has no intrinsic direction. Nestward progress disambiguates it
                // and prevents a carrier from following its own fresh trail back to food.
                if (!CanWalk(candidate) || cell < 0 || Navigation.HomeDistance[cell] >= Navigation.HomeDistance[current] || !Navigation.Clear(ant.Position, candidate)) continue;
                double signal = (Scent(candidate) + Scent(ant.Position + (candidate - ant.Position) * .5)) * .5;
                if (signal < .2) continue;
                double alignment = Math.Cos((candidate - ant.Position).Angle - direction);
                double score = Math.Log(1 + signal) * (1 + .15 * alignment) * (.95 + random.NextDouble() * .1);
                if (score > bestScore) { bestScore = score; best = candidate; }
            }
        }
        ant.ReturnTarget = best;
        return best;
    }
    void UpdateProgress(Worker ant)
    {
        if (ant.DetourTarget is Vec target && (Time >= ant.DetourUntil || !CanWalk(target) || !Navigation.Clear(ant.Position, target))) ant.DetourTarget = null;
        if (ant.ProgressOrigin is not Vec origin) { ant.ProgressOrigin = ant.Position; ant.ProgressTime = Time; return; }
        if (Time - ant.ProgressTime < 2) return;
        bool nearWall = Walls.Any(w => w.Contains(ant.Position, 30)) || ant.Position.X < 25 || ant.Position.X > Width - 25 || ant.Position.Y < 25 || ant.Position.Y > Height - 25;
        if (nearWall && (ant.Position - origin).Length < 18 && ant.DetourTarget == null && Time >= ant.DetourUntil)
        {
            ant.DetourTarget = Navigation.Detour(ant.Position, ant.Heading, ant.Id, p => VisibleFloor(p) && (NestExitOpen || !IsNestApron(p)));
            if (ant.DetourTarget != null) { ant.DetourUntil = Time + 3; ant.ReturnTarget = null; ant.Turn = 0; }
        }
        ant.ProgressOrigin = ant.Position; ant.ProgressTime = Time;
    }

    static double Angle(double a) => Math.Atan2(Math.Sin(a), Math.Cos(a));
    double? OutboundTrailTurn(Worker ant)
    {
        int current = Navigation.Index(ant.Position);
        if (current < 0 || Navigation.HomeDistance[current] < 0) return null;
        double min = double.MaxValue, max = 0, best = 0; Vec? target = null;
        for (int k = 0; k < 16; k++)
        {
            var point = ant.Position + Vec.Direction(ant.Heading + k * Math.PI / 8) * 24;
            if (!CanWalk(point) || !Navigation.Clear(ant.Position, point)) continue;
            double scent = (Scent(point) + Scent(ant.Position + (point - ant.Position) * .5)) * .5;
            min = Math.Min(min, scent); max = Math.Max(max, scent);
            int cell = Navigation.Index(point);
            if (cell < 0 || Navigation.HomeDistance[cell] <= Navigation.HomeDistance[current] || scent < .15) continue;
            double score = scent * (1 + .2 * Math.Cos((point - ant.Position).Angle - ant.Heading));
            if (score > best) { best = score; target = point; }
        }
        // A uniform field supplies no route and must not revive corner loops.
        return target is Vec next && max - min > .05 ? Angle((next - ant.Position).Angle - ant.Heading) * 4 : null;
    }
    void Tick(double dt)
    {
        Time += dt; nextSpawn -= dt;
        if (nextSpawn <= 0)
        {
            // Let existing crumbs and large food sources form lasting paths.
            if (Remaining <= 20 && Foods.Count < 2) SpawnFood();
            nextSpawn = Math.Max(120, SpawnInterval) * (1 + random.NextDouble() * .6);
        }
        double evaporation = Math.Exp(-Decay * dt);
        for (int i = 0; i < Pheromones.Length; i++) Pheromones[i] *= (float)evaporation;
        foreach (var ant in Workers)
        {
            ant.ClimbCooldown -= dt;
            if (ant.State == AntState.InNest)
            {
                ant.ProgressOrigin = null; ant.DetourTarget = null;
                ant.Position = Home; if (NestExitOpen) ant.Timer -= dt; ant.Energy = Math.Min(100, ant.Energy + dt * 18);
                if (NestExitOpen && ant.Timer <= 0 && ant.Energy > 60)
                {
                    ant.State = AntState.Searching;
                    var source = Foods.FirstOrDefault(f => f.Id == ant.FoodSourceId && f.Amount > 0);
                    if (source == null) ant.FoodSourceId = null;
                    ant.Heading = source != null ? (source.Position - Home).Angle + (random.NextDouble() - .5) * .2 : -Math.PI * random.NextDouble();
                }
                continue;
            }
            if (ant.State is AntState.Climbing or AntState.OnWindow or AntState.Descending)
            {
                ant.State = CanWalk(ant.Position) ? ant.HasFood || ant.Energy < 24 ? AntState.Returning : AntState.Searching : AntState.UnderWindow;
                ant.Elevation = 0; ant.ClimbWall = null; ant.EscapeTarget = null; ant.Decision = 0;
            }
            if (ant.State == AntState.UnderWindow) { UpdateCoveredWorker(ant, dt); continue; }
            ant.Energy -= dt * .3;
            if (ant.Energy < 24) ant.State = AntState.Returning;
            ant.Decision -= dt;
            UpdateProgress(ant);
            if (ant.State == AntState.Searching && ant.DetourTarget == null && ant.Decision <= 0 && Time >= ant.AvoidUntil)
            {
                ant.Decision = .22;
                var near = Foods.Where(f => f.Amount > 0 && (f.Position - ant.Position).Length < 95 && Navigation.Clear(ant.Position, f.Position)).OrderBy(f => (f.Position - ant.Position).Length).FirstOrDefault();
                if (near != null)
                {
                    if ((near.Position - ant.Position).Length < (near.IsLarge ? near.Radius + 3 : 12)) { near.Amount--; ant.FoodSourceId = near.Id; ant.HasFood = true; ant.State = AntState.Returning; ant.ReturnTarget = null; ant.Heading += Math.PI; }
                    else ant.Turn = Angle((near.Position - ant.Position).Angle - ant.Heading) * 4;
                }
                else
                {
                    // Uniform scent carries no directional information. Only a
                    // stronger, visible sample should override normal exploration.
                    double best = Scent(ant.Position) + .05, turn = (random.NextDouble() - .5) * 3.6;
                    foreach (double offset in new[] { -.65, 0, .65 })
                    {
                        var sample = ant.Position + Vec.Direction(ant.Heading + offset) * 24;
                        double signal = Navigation.Clear(ant.Position, sample) ? Scent(sample) : 0;
                        if (signal > best && random.NextDouble() > .12) { best = signal; turn = offset * 4; }
                    }
                    ant.Turn = OutboundTrailTurn(ant) ?? turn;
                }
            }
            if (ant.State == AntState.Returning)
            {
                if (NestExitOpen && (ant.Position - Home).Length < 27)
                {
                    if (ant.HasFood) { Delivered++; Stored++; ant.HasFood = false; }
                    if (ant.Energy < 50 && Stored > 0) { Stored--; Consumed++; }
                    ant.State = AntState.InNest; ant.ReturnTarget = null; ant.Timer = 2 + random.NextDouble() * 3; continue;
                }
                var target = ant.DetourTarget ?? ReturnDirection(ant);
                ant.Heading = (target - ant.Position).Angle; ant.Turn = 0;
                if (ant.HasFood)
                {
                    int i = Navigation.Index(ant.Position);
                    if (i >= 0) Pheromones[i] = Math.Min(24, Pheromones[i] + (float)(8 * dt));
                }
            }
            if (ant.DetourTarget is Vec detour) { ant.Heading = (detour - ant.Position).Angle; ant.Turn = 0; }
            ant.Heading = Angle(ant.Heading + Math.Clamp(ant.Turn, -5, 5) * dt);
            double distance = (ant.HasFood ? 36 : 48) * dt;
            if (ant.DetourTarget is Vec waypoint) distance = Math.Min(distance, (waypoint - ant.Position).Length);
            var next = ant.Position + Vec.Direction(ant.Heading) * distance;
            if (CanWalk(next) && Navigation.Clear(ant.Position, next)) { ant.Position = next; ant.Distance += distance; }
            else
            {
                ant.DetourTarget = null;
                ant.ReturnTarget = null;
                bool turned = false;
                // Try small turns first, so walls produce perimeter following rather than random penetration.
                foreach (double offset in new[] { .55, -.55, 1.1, -1.1, 1.57, -1.57, Math.PI })
                {
                    var candidate = ant.Position + Vec.Direction(ant.Heading + offset) * Math.Max(6, distance);
                    if (!CanWalk(candidate) || !Navigation.Clear(ant.Position, candidate)) continue;
                    ant.Heading = Angle(ant.Heading + offset); ant.Turn = 0; ant.AvoidUntil = Time + .8; ant.Decision = .8;
                    var step = ant.Position + Vec.Direction(ant.Heading) * distance;
                    if (CanWalk(step) && Navigation.Clear(ant.Position, step)) { ant.Position = step; ant.Distance += distance; }
                    turned = true; break;
                }
                if (!turned) ant.Heading = Angle(ant.Heading + Math.PI * .6);
            }
            if (ant.DetourTarget is Vec reached && (reached - ant.Position).Length < 2)
            {
                ant.DetourTarget = null; ant.ProgressOrigin = ant.Position; ant.ProgressTime = Time;
                ant.AvoidUntil = Time + 1; ant.Decision = 1; ant.Turn = 0;
            }
        }
        Foods.RemoveAll(f => f.Amount <= 0);
        Development.Advance(dt, Workers.Count, Workers.Count == 0 ? 0 : Workers.Average(a => a.Energy), Stored);
        UpdateNursery(dt);
    }
    void UpdateCoveredWorker(Worker ant, double dt)
    {
        if (CanWalk(ant.Position))
        {
            ant.State = ant.HasFood || ant.Energy < 24 ? AntState.Returning : AntState.Searching;
            ant.EscapeTarget = null; ant.Decision = 0; return;
        }
        ant.Energy = Math.Max(0, ant.Energy - dt * .3); ant.Decision -= dt;
        if (ant.Decision <= 0 || ant.EscapeTarget == null)
        {
            // The target guides locomotion under the window; it is not assigned as a position.
            ant.EscapeTarget = Navigation.Nearest(ant.Position, p => Free(p) && VisibleFloor(p) && (NestExitOpen || !IsNestApron(p)), false);
            ant.Decision = .35;
        }
        if (ant.EscapeTarget is not Vec target) return;
        var delta = target - ant.Position;
        double travel = Math.Min(delta.Length, (ant.HasFood ? 36 : 48) * dt);
        if (travel <= 0) return;
        ant.Heading = delta.Angle;
        var step = ant.Position + delta.Normalized * travel;
        if (!OnScreen(step)) return;
        ant.Position = step; ant.Distance += travel;
        if (CanWalk(ant.Position))
        {
            ant.State = ant.HasFood || ant.Energy < 24 ? AntState.Returning : AntState.Searching;
            ant.EscapeTarget = null; ant.Decision = 0;
        }
    }
    void UpdateNursery(double dt)
    {
        metabolism += dt; eggClock += dt;
        if (metabolism >= 25) { metabolism = 0; if (Stored > 0) { Stored--; Consumed++; } }
        if (eggClock >= 40 && Stored > 2 && Nursery.Count < 18 && Workers.Count < 120)
        { eggClock = 0; Stored--; Consumed++; Nursery.Add(new Brood()); }
        foreach (var brood in Nursery.ToArray())
        {
            if (Stored <= 0) continue;
            brood.Age += dt;
            double duration = brood.Stage == BroodStage.Egg ? 55 : brood.Stage == BroodStage.Larva ? 70 : 85;
            if (brood.Age < duration) continue;
            if (brood.Stage == BroodStage.Pupa)
            {
                if (Workers.Count >= 120) continue;
                Nursery.Remove(brood); AddWorker(); Births++;
            }
            else { brood.Stage++; brood.Age = 0; Stored--; Consumed++; }
        }
    }
}
