namespace AntField.Core;

public sealed record WorkerSnapshot(int Id, double X, double Y, double Heading, double Distance, double Energy, bool HasFood, bool InNest, double Timer, int? FoodSourceId = null, AntState? State = null);
public sealed record FoodSnapshot(int Id, double X, double Y, int Amount, int Capacity = 0);
public sealed record BroodSnapshot(BroodStage Stage, double Age);
public sealed record ColonySnapshot(int Version, double Width, double Height, double Time, int Delivered, int Stored, int Consumed, int Spawned, int Expired, int Births, double ClimbProbability, double SpawnInterval, WorkerSnapshot[] Workers, FoodSnapshot[] Foods, BroodSnapshot[] Nursery, Wall[]? Screens = null, Vec? Home = null, float[]? Pheromones = null, double NextSpawn = 3, double EggClock = 0, double Metabolism = 0, double NestWorkHours = 0);
