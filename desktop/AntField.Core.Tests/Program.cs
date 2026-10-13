using System.Text.Json;
using AntField.Core;

int passed = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
void Test(string name, Action test) { test(); passed++; Console.WriteLine($"PASS {name}"); }
void Conserved(DesktopColony c)
{
    Check(c.Delivered + c.Carrying + c.Remaining + c.Expired == c.Spawned, "Food is not conserved");
    Check(c.Stored + c.Consumed == 12 + c.Delivered, "Nest resources are not conserved");
}
Test("Food appears only on reachable, window-free desktop", () => {
    var c = new DesktopColony(1000, 700); c.UpdateWalls([new Wall(1, 280, 150, 400, 380)]);
    for (int i = 0; i < 30; i++) c.SpawnFood();
    Check(c.Foods.Count > 0, "No food generated");
    Check(c.Foods.All(f => c.CanWalk(f.Position) && !c.Walls.Any(w => w.Contains(f.Position, 7))), "Food spawned inside a window"); Conserved(c);
});
Test("Workers forage, navigate walls, deliver and conserve food", () => {
    var c = new DesktopColony(900, 650) { ClimbProbability = 0 };
    c.UpdateWalls([new Wall(1, 300, 170, 290, 280)]);
    for (int i = 0; i < 5; i++) c.SpawnFood();
    for (int i = 0; i < 3600; i++) {
        c.Step(.1); Conserved(c);
        Check(c.Workers.All(a => a.State == AntState.InNest || c.CanWalk(a.Position)), "A floor ant entered a wall");
    }
    Console.WriteLine($"  Delivered {c.Delivered}, carrying {c.Carrying}, births {c.Births}");
    Check(c.Delivered >= 15, "Workers did not deliver enough food");
    Check(c.Pheromones.Any(v => v > .2), "No pheromone trail");
    Check(c.Births > 0, "No full brood lifecycle");
});
Test("A fully covered desktop suppresses random food", () => {
    var c = new DesktopColony(900, 650); c.UpdateWalls([new Wall(1, 0, 0, 900, 650)]);
    for (int i = 0; i < 8; i++) Check(!c.SpawnFood(), "Food appeared over a maximized window");
    Check(!c.FoodSpaceAvailable, "Missing no-space status");
    c.Step(30); Check(c.Remaining == 0, "Food appeared while desktop was covered"); Conserved(c);
});
Test("Moving windows preserve positions while covered ants crawl underneath", () => {
    var c = new DesktopColony(1000, 700); for (int i = 0; i < 5; i++) c.SpawnFood(); c.Step(20);
    var food = c.Foods[0]; var point = food.Position;
    var positions = c.Workers.ToDictionary(a => a.Id, a => a.Position);
    c.UpdateWalls([new Wall(1, point.X - 35, point.Y - 35, 70, 70)]);
    Check(c.Foods.All(f => c.CanWalk(f.Position)), "Covered food survived");
    Check(c.Workers.All(a => a.Position == positions[a.Id]), "Window movement teleported a worker");
    Check(c.Workers.All(a => a.State is AntState.InNest or AntState.UnderWindow || c.CanWalk(a.Position)), "A covered ant did not enter the underground state"); Conserved(c);
    c.UpdateWalls([]); Check(c.SpawnFood(), "Food did not recover after closing a window"); Conserved(c);
});
Test("A covered carrier retains food and emerges by walking at normal speed", () => {
    var c = new DesktopColony(1000, 700, 1) { ClimbProbability = 0 };
    c.SpawnFood(); var food = c.Foods[0]; var ant = c.Workers[0];
    ant.Position = food.Position; ant.State = AntState.Searching; ant.Decision = 0;
    c.Step(.01); Check(ant.HasFood, "Worker did not pick up food");
    var original = ant.Position;
    c.UpdateWalls([new Wall(1, original.X - 60, original.Y - 60, 120, 120)]);
    Check(ant.Position == original && ant.State == AntState.UnderWindow && ant.HasFood, "A covered carrier was moved or lost its food");
    int delivered = c.Delivered;
    for (int i = 0; i < 200 && ant.State == AntState.UnderWindow; i++)
    {
        var previous = ant.Position; c.Step(.1);
        Check((ant.Position - previous).Length <= 3.600001, "The covered carrier teleported or exceeded carrying speed");
        Check(ant.HasFood && c.Delivered == delivered, "Food was delivered while still escaping"); Conserved(c);
    }
    Check(ant.State == AntState.Returning && c.CanWalk(ant.Position), "The carrier failed to emerge and resume returning");
});
Test("Repeated coverage and uncovering preserve the current ground position", () => {
    var c = new DesktopColony(1000, 700, 1); var ant = c.Workers[0];
    ant.Position = new Vec(300, 300); ant.State = AntState.Searching;
    c.UpdateWalls([new Wall(1, 240, 240, 160, 160)]); c.Step(.25); var current = ant.Position;
    c.UpdateWalls([new Wall(1, current.X - 55, current.Y - 55, 160, 160), new Wall(2, current.X - 30, current.Y - 30, 120, 120)]);
    Check(ant.Position == current && ant.State == AntState.UnderWindow, "Overlapping moving windows shifted the worker");
    c.UpdateWalls([]); Check(ant.Position == current && ant.State == AntState.Searching, "Uncovering moved the worker");
});
Test("Bottom workers stay on the bottom floor and crawl toward the nest apron", () => {
    var c = new DesktopColony(1000, 700, 1) { ClimbProbability = 0 }; var ant = c.Workers[0];
    ant.Position = new Vec(390, 678); ant.State = AntState.Searching;
    var original = ant.Position; c.UpdateWalls([new Wall(1, 330, 550, 170, 150)]);
    Check(ant.Position == original && ant.State == AntState.UnderWindow, "A bottom worker was pushed upward or away");
    for (int i = 0; i < 100 && ant.State == AntState.UnderWindow; i++)
    {
        var previous = ant.Position; c.Step(.1);
        Check((ant.Position - previous).Length <= 4.800001 && ant.Position.Y > 650, "Bottom escape changed position abruptly or left the bottom floor");
    }
    Check(c.CanWalk(ant.Position) && ant.State != AntState.UnderWindow, "The worker did not crawl out at the bottom apron");
});
Test("Window walls remain solid and workers never climb, even with legacy settings", () => {
    var c = new DesktopColony(900, 650, 120) { ClimbProbability = .1 };
    c.UpdateWalls([new Wall(1, 300, 100, 300, 350)]);
    foreach (var ant in c.Workers) { ant.Position = new Vec(280, 250); ant.State = AntState.Searching; ant.Heading = 0; ant.Decision = 999; }
    for (int i = 0; i < 100; i++) {
        c.Step(.1);
        Check(c.Workers.All(a => a.State is not (AntState.Climbing or AntState.OnWindow or AntState.Descending) && a.Elevation == 0), "A worker climbed onto a window");
        Check(c.Workers.All(a => a.State == AntState.InNest || c.CanWalk(a.Position)), "A worker penetrated the window wall");
    }
    var saved = c.Snapshot() with { ClimbProbability = .05 };
    Check(DesktopColony.Restore(saved, c.Width, c.Height).ClimbProbability == 0, "Legacy save enabled climbing");
    Conserved(c);
});
Test("Legacy climbing states return to the ground without teleporting", () => {
    foreach (var state in new[] { AntState.Climbing, AntState.OnWindow, AntState.Descending }) {
        var c = new DesktopColony(900, 650, 1); var wall = new Wall(1, 300, 100, 300, 350);
        c.UpdateWalls([wall]); var ant = c.Workers[0];
        ant.Position = new Vec(400, 250); ant.State = state; ant.ClimbWall = wall; ant.Elevation = 1;
        var position = ant.Position; c.Step(.1);
        Check(ant.State == AntState.UnderWindow && ant.Elevation == 0 && ant.ClimbWall == null, "Legacy climber remained on the window");
        Check((ant.Position - position).Length <= 4.800001, "Legacy climber teleported");
    }
});
Test("Saved colonies restore brood, food, carriers and resources", () => {
    var c = new DesktopColony(900, 650); for (int i = 0; i < 4; i++) c.SpawnFood(); c.Step(80);
    var saved = JsonSerializer.Deserialize<ColonySnapshot>(JsonSerializer.Serialize(c.Snapshot()))!;
    var restored = DesktopColony.Restore(saved, 1100, 750);
    Check(restored.Workers.Count == c.Workers.Count && restored.Nursery.Count == c.Nursery.Count, "Population was lost");
    Check(restored.Delivered == c.Delivered && restored.Stored == c.Stored, "Resources were lost");
    Conserved(restored); restored.Step(15); Conserved(restored);
});
Test("A newly disconnected food pocket is removed rather than falsely delivered", () => {
    var c = new DesktopColony(900, 650, 0); c.SpawnFood(); var f = c.Foods[0]; int before = c.Delivered;
    c.UpdateWalls([new Wall(1, 0, 0, 900, 650)]);
    Check(c.Remaining == 0 && c.Delivered == before && c.Expired > 0, "Disconnected food was falsely delivered"); Conserved(c);
});
Test("Carriers escape all four window corners and return without teleporting", () => {
    var wall = new Wall(1, 280, 150, 350, 330);
    foreach (var corner in new[] { new Vec(270, 140), new Vec(640, 140), new Vec(270, 490), new Vec(640, 490) })
    foreach (double angle in new[] { 0.0, Math.PI / 2, Math.PI, -Math.PI / 2 })
    {
        var c = new DesktopColony(1000, 700, 1) { ClimbProbability = 0 }; c.UpdateWalls([wall]);
        var ant = c.Workers[0]; ant.Position = corner; ant.State = AntState.Returning; ant.HasFood = true; ant.Heading = angle;
        Check(c.CanWalk(corner), "Invalid corner test position");
        for (int tick = 0; tick < 1200 && ant.HasFood; tick++)
        {
            var before = ant.Position; c.Step(.05);
            Check(ant.State == AntState.InNest || (ant.Position - before).Length <= 2.400001, "A corner worker teleported");
            Check(c.CanWalk(ant.Position), "A corner route penetrated the window");
        }
        Check(!ant.HasFood && c.Delivered == 1, $"A carrier became stuck: {corner} -> {ant.Position}, target {c.Navigation.HomeTarget(ant.Position,c.Home)}");
    }
});
Test("Explorers at all four corners keep walking instead of oscillating", () => {
    foreach (var corner in new[] { new Vec(270, 140), new Vec(640, 140), new Vec(270, 490), new Vec(640, 490) })
    {
        var c = new DesktopColony(1000, 700, 1) { ClimbProbability = 0 }; c.UpdateWalls([new Wall(1, 280, 150, 350, 330)]);
        var ant = c.Workers[0]; ant.Position = corner; ant.State = AntState.Searching; ant.Heading = (new Vec(455, 315) - corner).Angle;
        c.Foods.Add(new FoodPatch { Id = 999, Position = new Vec(455, 315), Amount = 100 }); // A scent behind the wall must not trap the explorer.
        double maxDistance = 0;
        for (int tick = 0; tick < 200; tick++) { c.Step(.05); maxDistance = Math.Max(maxDistance, (ant.Position - corner).Length); Check(c.CanWalk(ant.Position), "Explorer entered the wall"); }
        Check(maxDistance > 50 && ant.Distance > 150, "Explorer remained trapped at the corner");
    }
});
Test("Multi-screen offsets, seams, taskbars and empty layout gaps", () => {
    var c = new DesktopColony(2000, 1400, 1) { ClimbProbability = 0 };
    // Local coordinates for a secondary screen physically left and above the primary.
    var screens = new[] { new Wall(0, 0, 0, 1000, 900), new Wall(1, 1000, 200, 1000, 1200) };
    var home = new Vec(1500, 1376); c.ConfigureScreens(2000, 1400, screens, home);
    Check(c.Home == home && !c.Free(new Vec(500, 1200)) && !c.Free(new Vec(1500, 100)), "Screen gaps became walkable");
    Check(c.Navigation.Reachable(new Vec(500, 400)), "Secondary monitor is unreachable across the seam");
    for (int i = 0; i < 9; i++) c.SpawnFood();
    Check(c.Foods.All(f => c.OnScreen(f.Position)) && c.Foods.Any(f => f.Position.X < 1000), "Food did not support secondary screen");
    var ant = c.Workers[0]; ant.Position = new Vec(500, 400); ant.State = AntState.Returning; ant.HasFood = true;
    for (int i = 0; i < 2400 && ant.HasFood; i++) { c.Step(.05); Check(c.OnScreen(ant.Position), "Ant crossed a monitor gap"); }
    Check(!ant.HasFood, "Ant could not return across monitors");
    ant.State = AntState.Searching; var position = ant.Position;
    c.ConfigureScreens(2200, 1500, screens.Select(s => s with { X = s.X + 200, Y = s.Y + 100 }), home + new Vec(200, 100), new Vec(200, 100));
    Check(ant.Position == position + new Vec(200, 100), "Changing virtual origin moved the ant physically");
});
Test("Stacked mixed-size screens cross taskbar seams, restore positions and handle unplugging", () => {
    var c = new DesktopColony(1800, 1800, 1) { ClimbProbability = 0 };
    c.FoodAreas = [new Wall(0, 0, 0, 1200, 760), new Wall(1, 0, 800, 1800, 960)];
    c.ConfigureScreens(1800, 1800, [new Wall(0, 0, 0, 1200, 800), new Wall(1, 0, 800, 1800, 1000)], new Vec(900, 1736));
    Check(c.Navigation.Reachable(new Vec(500, 400)), "Top monitor is disconnected by its taskbar");
    for (int i = 0; i < 9; i++) c.SpawnFood();
    Check(c.Foods.All(f => c.FoodAreas.Any(s => s.Contains(f.Position, -14))), "Food spawned on a taskbar");
    var ant = c.Workers[0]; ant.Position = new Vec(500, 400); ant.State = AntState.Returning;
    var snapshot = JsonSerializer.Deserialize<ColonySnapshot>(JsonSerializer.Serialize(c.Snapshot()))!;
    var restored = DesktopColony.Restore(snapshot, c.Width, c.Height);
    Check(restored.Home == c.Home && restored.Workers[0].Position == ant.Position && restored.Screens.SequenceEqual(c.Screens), "Multi-screen save lost its layout or worker position");
    c.FoodAreas = [new Wall(0, 0, 0, 1800, 960)];
    c.ConfigureScreens(1800, 1000, [new Wall(0, 0, 0, 1800, 1000)], new Vec(900, 936), new Vec(0, -800));
    Check(c.OnScreen(ant.Position) && ant.Position.Y >= 7, "Unplugged monitor left a worker outside the desktop");
    c.Step(1); Check(c.OnScreen(ant.Position), "Worker remained stranded after unplugging");
});
Test("Carriers choose different nestward routes when the local scent changes", () => {
    double Run(int side) {
        var c = new DesktopColony(900, 650, 1); var ant = c.Workers[0];
        ant.Position = new Vec(450, 250); ant.State = AntState.Returning; ant.HasFood = true;
        for (int i = 0; i < c.Pheromones.Length; i++) {
            var point = c.Navigation.Center(i);
            if ((point.X - 450) * side > 8 && (point.X - 450) * side < 55 && point.Y > 250 && point.Y < 310) c.Pheromones[i] = 20;
        }
        c.Step(.5);
        Check(ant.HasFood && c.CanWalk(ant.Position), "Carrier lost food or left the floor");
        return ant.Position.X;
    }
    Check(Run(-1) < 445 && Run(1) > 455, "Changing scent did not change the return route");
});
Test("Carriers ignore scent behind walls and keep returning after a trail is blocked", () => {
    var c = new DesktopColony(1000, 700, 1); var ant = c.Workers[0];
    var wall = new Wall(1, 280, 150, 350, 330); c.UpdateWalls([wall]);
    ant.Position = new Vec(270, 140); ant.State = AntState.Returning; ant.HasFood = true;
    for (int i = 0; i < c.Pheromones.Length; i++) c.Pheromones[i] = 20;
    c.Step(.3);
    var position = ant.Position;
    c.UpdateWalls([wall, new Wall(2, 100, 350, 120, 100)]);
    Check(ant.Position == position && ant.ReturnTarget == null, "Layout change retained a stale waypoint or moved the ant");
    for (int i = 0; i < 1600 && ant.HasFood; i++) {
        var before = ant.Position; c.Step(.05);
        Check(c.CanWalk(ant.Position), "Carrier followed scent through a wall");
        Check(ant.State == AntState.InNest || (ant.Position - before).Length <= 1.800001, "Carrier teleported");
    }
    Check(!ant.HasFood && c.Delivered == 1, "Scent-following carrier failed to return");
});
Test("Free corner strips connect to routing samples even when their cell centers are blocked", () => {
    var c = new DesktopColony(1000, 700, 1);
    c.UpdateWalls([new Wall(1, 262.1, 142.3, 351.7, 295.3)]);
    var start = new Vec(612.26, 448.67); var end = new Vec(595, 448.67);
    Check(c.Navigation.HomeDistance[c.Navigation.Index(end)] < 0, "The test no longer exercises a blocked cell center");
    Check(c.Free(end) && c.CanWalk(end), "A physically clear strip was treated as an impassable grid wall");
    Check(c.Navigation.Clear(start, end), "The clear bottom strip was blocked");
    Check(!c.CanWalk(new Vec(603, 441)), "Corner anchoring allowed entry into the real window margin");
    var ant = c.Workers[0]; ant.Position = start; ant.State = AntState.Searching; ant.Heading = Math.PI; ant.Decision = 100; ant.Turn = 0;
    c.Step(.4);
    Check(ant.Position.X < 600 && c.CanWalk(ant.Position), "An ant could not walk across the clear strip");
});
Test("Dense colonies escape fractional window corners in uniform scent without teleporting", () => {
    for (int layout = 0; layout < 8; layout++) foreach (bool carrying in new[] { false, true })
    {
        var wall = new Wall(1, 260 + layout * 2.1, 141 + layout * 1.3, 351.7, 295.3);
        var c = new DesktopColony(1000, 700, 120, layout); c.UpdateWalls([wall]);
        var corners = new List<Vec>();
        foreach (int sx in new[] { -1, 1 }) foreach (int sy in new[] { -1, 1 })
        foreach (double offset in new[] { 7.05, 7.5, 8, 9, 11, 14, 18, 22 })
        {
            var p = new Vec((sx < 0 ? wall.X : wall.X + wall.Width) + sx * offset, (sy < 0 ? wall.Y : wall.Y + wall.Height) + sy * offset);
            if (c.CanWalk(p)) corners.Add(p);
        }
        foreach (var ant in c.Workers)
        {
            ant.Position = corners[ant.Id % corners.Count]; ant.State = carrying ? AntState.Returning : AntState.Searching;
            ant.HasFood = carrying; ant.Heading = (new Vec(wall.X + wall.Width / 2, wall.Y + wall.Height / 2) - ant.Position).Angle;
        }
        var origins = c.Workers.Select(a => a.Position).ToArray();
        var furthest = new double[120]; var delivered = new bool[120];
        Array.Fill(c.Pheromones, 20);
        for (int tick = 0; tick < (carrying ? 1200 : 240); tick++)
        {
            var previous = c.Workers.Select(a => a.Position).ToArray();
            var speeds = c.Workers.Select(a => a.HasFood ? 36 : 48).ToArray();
            c.Step(.05);
            for (int i = 0; i < 120; i++)
            {
                var ant = c.Workers[i];
                Check(ant.State == AntState.InNest || (ant.Position - previous[i]).Length <= speeds[i] * .05 + .000001, "A corner escape teleported or exceeded walking speed");
                Check(c.CanWalk(ant.Position), "A corner escape penetrated a window");
                furthest[i] = Math.Max(furthest[i], (ant.Position - origins[i]).Length);
                if (!ant.HasFood) delivered[i] = true;
            }
        }
        Check(carrying ? delivered.All(done => done) : furthest.All(distance => distance > 60), $"Corner loop survived in layout {layout}, carrying {carrying}");
    }
});
Test("Occasional large morsels stay finite, fit the desktop and survive save reload", () => {
    int largeCount = 0, smallCount = 0;
    for (int seed = 0; seed < 30; seed++)
    {
        var c = new DesktopColony(1000, 700, 0, seed);
        c.UpdateWalls([new Wall(1, 280, 150, 400, 380)]);
        for (int i = 0; i < 9; i++) c.SpawnFood();
        Check(c.Foods.Count(f => f.IsLarge) <= 1, "Large sources crowded out ordinary food");
        foreach (var food in c.Foods)
        {
            if (food.IsLarge)
            {
                largeCount++;
                Check(food.Amount is >= 160 and <= 280, "Large source has invalid supply");
                Check(!c.Walls.Any(w => w.Contains(food.Position, 30)), "Large morsel overlaps a window");
                Check(food.Position.X >= 30 && food.Position.Y >= 30 && food.Position.X < c.Width - 30 && food.Position.Y < c.Height - 30, "Large morsel crosses the screen edge");
            }
            else { smallCount++; Check(food.Amount is >= 8 and <= 19, "Ordinary crumbs changed size"); }
        }
        Conserved(c);
        var snapshot = c.Snapshot();
        var restored = DesktopColony.Restore(JsonSerializer.Deserialize<ColonySnapshot>(JsonSerializer.Serialize(snapshot))!, c.Width, c.Height);
        Check(restored.Foods.Select(f => (f.Id, f.Amount, f.Capacity)).SequenceEqual(c.Foods.Select(f => (f.Id, f.Amount, f.Capacity))), "Reload lost source size or supply"); Conserved(restored);
        if (c.Foods.FirstOrDefault(f => f.IsLarge) is { } large)
        {
            var p = large.Position;
            c.UpdateWalls([new Wall(2, p.X + 20, p.Y - 40, 100, 80)]);
            Check(c.Foods.All(f => f.Id != large.Id), "A window touching a large morsel left it floating over the window"); Conserved(c);
        }
    }
    Check(largeCount >= 10 && smallCount > largeCount * 3, "Large sources do not appear occasionally alongside crumbs");
    var old = new DesktopColony(800, 600, 0).Snapshot() with { Foods = [new FoodSnapshot(1, 200, 200, 12)], Spawned = 12 };
    var legacy = DesktopColony.Restore(old, 800, 600);
    Check(!legacy.Foods[0].IsLarge && legacy.Foods[0].Capacity == 12, "Old saves no longer restore ordinary crumbs"); Conserved(legacy);
});
Test("A scout repeatedly harvests a large source and maintains a real return trail", () => {
    var original = new DesktopColony(900, 650, 1);
    var source = original.Home + new Vec(0, -250);
    var snapshot = original.Snapshot() with {
        Foods = [new FoodSnapshot(999, source.X, source.Y, 200, 200)], Spawned = 200,
        Workers = [new WorkerSnapshot(0, source.X + 22, source.Y, Math.PI, 0, 100, false, false, 0)]
    };
    var c = DesktopColony.Restore(snapshot, 900, 650); c.SpawnInterval = 1e9;
    var scout = c.Workers[0]; int pickups = 0; bool carrying = false;
    for (int tick = 0; tick < 900 && pickups < 3; tick++)
    {
        c.Step(.05); Conserved(c);
        if (scout.HasFood && !carrying && scout.FoodSourceId == 999) pickups++;
        carrying = scout.HasFood;
    }
    var food = c.Foods.Single(f => f.Id == 999);
    Check(pickups >= 3 && c.Delivered >= 2 && food.Amount <= 197 && food.Amount > 0, "Scout did not establish repeated finite harvest trips");
    Check(food.IsLarge && food.Radius < 26, "Harvested source did not shrink while remaining large");
    int marked = Enumerable.Range(0, c.Pheromones.Length).Count(i => {
        var p = c.Navigation.Center(i);
        return Math.Abs(p.X - c.Home.X) < 30 && p.Y > source.Y + 30 && p.Y < c.Home.Y - 30 && c.Pheromones[i] > .15;
    });
    Check(marked >= 8, "Repeated carriers left no sustained path between food and nest");
});
Test("A discovered large source recruits multiple workers into repeated harvest trips", () => {
    var original = new DesktopColony(900, 650, 24);
    var source = original.Home + new Vec(0, -250);
    var snapshot = original.Snapshot() with {
        Foods = [new FoodSnapshot(999, source.X, source.Y, 280, 280)], Spawned = 280,
        Workers = original.Snapshot().Workers.Select((w, i) => i == 0
            ? w with { X = source.X, Y = source.Y, InNest = false, Timer = 0 }
            : w with { Timer = 12 + i * .05 }).ToArray()
    };
    var c = DesktopColony.Restore(snapshot, 900, 650); c.SpawnInterval = 1e9;
    var recruited = new HashSet<int>();
    for (int tick = 0; tick < 600; tick++)
    {
        c.Step(.1); Conserved(c);
        foreach (var ant in c.Workers.Where(a => a.HasFood && a.FoodSourceId == 999)) recruited.Add(ant.Id);
    }
    Check(recruited.Count >= 6 && c.Foods.Single(f => f.Id == 999).Amount < 260, "The source remained a lone scout's route instead of recruiting a colony");
});
Test("An empty worker follows a local food trail outward without knowing the source", () => {
    var c = new DesktopColony(900, 650, 1) { SpawnInterval = 1e9 };
    var ant = c.Workers[0]; ant.Position = c.Home + new Vec(0, -80); ant.State = AntState.Searching; ant.Heading = Math.PI / 2;
    for (int i = 0; i < c.Pheromones.Length; i++)
    {
        var p = c.Navigation.Center(i);
        if (Math.Abs(p.X - c.Home.X) < 18 && p.Y < c.Home.Y - 20 && p.Y > c.Home.Y - 270) c.Pheromones[i] = 4;
    }
    var start = ant.Position;
    c.Step(2);
    Check(ant.FoodSourceId == null && ant.Position.Y < start.Y - 45 && Math.Abs(ant.Position.X - start.X) < 35, "An empty worker followed the trail into the nest or needed a global food target");
    Check(c.CanWalk(ant.Position) && !ant.HasFood, "Trail following left the floor or invented food");
});
Test("Progress restores trails, food memory, fatigue and colony clocks", () => {
    var c = new DesktopColony(900, 650, 3); c.Step(18.25);
    c.Workers[0].State = AntState.InNest; c.Workers[0].Timer = 7.25; c.Workers[0].FoodSourceId = c.Foods[0].Id;
    c.Workers[1].State = AntState.Returning; c.Workers[1].HasFood = false; c.Workers[1].Energy = 18;
    for (int i = 0; i < c.Pheromones.Length; i++) if (c.Navigation.HomeDistance[i] >= 0) c.Pheromones[i] = (i % 13) / 2f;
    var saved = JsonSerializer.Deserialize<ColonySnapshot>(JsonSerializer.Serialize(c.Snapshot()))!;
    var restored = DesktopColony.Restore(saved, c.Width, c.Height); var after = restored.Snapshot();
    Check(after.Time == saved.Time && after.Delivered == saved.Delivered && after.Births == saved.Births && after.Stored == saved.Stored && after.Consumed == saved.Consumed, "Cumulative progress was lost");
    Check(after.NextSpawn == saved.NextSpawn && after.EggClock == saved.EggClock && after.Metabolism == saved.Metabolism, "Colony timers restarted");
    Check(restored.Workers[0].Timer == 7.25 && restored.Workers[0].FoodSourceId == c.Workers[0].FoodSourceId, "Rest or food-source memory restarted");
    Check(restored.Workers[1].State == AntState.Returning && restored.Workers[1].Energy == 18, "Fatigue return state was lost");
    Check(restored.Pheromones.SequenceEqual(c.Pheromones), "Saved paths disappeared");
    var resized = DesktopColony.Restore(saved, 1100, 750); Check(resized.Pheromones.All(v => v == 0), "Resizing pasted trails into unrelated cells");
    var old = saved with { Version = 1, Pheromones = null }; Check(DesktopColony.Restore(old, c.Width, c.Height).Time == c.Time, "Legacy progress no longer loads");
});
Test("Progress files migrate legacy saves, recover backups and archive resets", () => {
    string folder = Path.Combine(Path.GetTempPath(), "AntField-progress-test-" + Guid.NewGuid().ToString("N"));
    try
    {
        var store = new ProgressStore(folder); Check(store.Load() == null && !store.LoadFailed, "A new player was marked as corrupted");
        Directory.CreateDirectory(folder);
        var colony = new DesktopColony(900, 650); colony.Step(17);
        File.WriteAllText(store.PathName, JsonSerializer.Serialize(colony.Snapshot() with { Version = 1 }));
        var legacy = store.Load()!; Check(legacy.Colony.Time == colony.Time, "Existing save was not migrated");
        var first = legacy with { SavedAt = DateTimeOffset.UtcNow, Speed = 2, Paused = true, ShowTrails = true };
        store.Save(first); colony.Step(10);
        var second = first with { SavedAt = first.SavedAt.AddSeconds(10), Colony = colony.Snapshot() }; store.Save(second);
        Check(store.Load() == second || JsonSerializer.Serialize(store.Load()) == JsonSerializer.Serialize(second), "Progress or preferences failed to round-trip");
        File.WriteAllText(store.PathName, "broken");
        var recovered = store.Load()!; Check(store.RecoveredBackup && recovered.Colony.Time == first.Colony.Time && recovered.RunId == first.RunId, "Corruption discarded the available backup");
        store.Save(second);
        var next = new SavedProgress(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, new DesktopColony(900, 650).Snapshot());
        store.Reset(second, next);
        Check(store.Load()!.RunId == next.RunId && store.Load()!.Colony.Time == 0, "Reset failed to persist the new run immediately");
        var history = store.History(); Check(history.Length == 1 && history[0].RunId == second.RunId && history[0].Colony.Time == second.Colony.Time, "Reset erased the previous run instead of recording it");
        Conserved(DesktopColony.Restore(history[0].Colony, 900, 650));
    }
    finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
});
Test("Failed reset persistence keeps the active progress intact", () => {
    string folder = Path.Combine(Path.GetTempPath(), "AntField-progress-failure-" + Guid.NewGuid().ToString("N"));
    try
    {
        var store = new ProgressStore(folder); var c = new DesktopColony(900, 650); c.Step(13);
        var previous = new SavedProgress(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, c.Snapshot()); store.Save(previous);
        File.WriteAllText(Path.Combine(folder, "history"), "blocked-directory");
        bool failed = false;
        try { store.Reset(previous, previous with { RunId = Guid.NewGuid().ToString("N"), Colony = new DesktopColony(900, 650).Snapshot() }); }
        catch (IOException) { failed = true; }
        Check(failed && store.Load()!.RunId == previous.RunId && store.Load()!.Colony.Time == previous.Colony.Time, "A failed reset overwrote active progress");
    }
    finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
});
Console.WriteLine($"{passed} desktop simulation checks passed.");
