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
Console.WriteLine($"{passed} desktop simulation checks passed.");
