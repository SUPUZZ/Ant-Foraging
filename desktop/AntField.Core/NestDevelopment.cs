namespace AntField.Core;

// A cumulative labour budget, rather than a fixed list of upgrade levels.
// Each successive chamber takes slightly more excavation; strata are generated
// on demand so a long-running colony need not keep every mesh in memory.
public sealed class NestDevelopment
{
    public double WorkHours { get; private set; }
    public const int QueenCount = 1;
    public long CompletedRooms => (long)Math.Floor(WorkHours / (1.06 + Math.Sqrt(1.1236 + .12 * WorkHours)));
    static double Cost(long rooms) => 2.12 * rooms + .12 * (double)rooms * rooms;
    public double RoomProgress => Math.Clamp((WorkHours - Cost(CompletedRooms)) / (2.24 + .24 * CompletedRooms), 0, 1);
    public long DeepestLayer => CompletedRooms / 4 + (CompletedRooms % 4 > 0 || RoomProgress >= .05 ? 1 : 0);
    public double ExitScale => 1 + .8 * (1 - Math.Exp(-WorkHours / 72));

    public NestDevelopment(double workHours = 0)
    {
        if (!double.IsFinite(workHours) || workHours < 0 || workHours > 1e18) throw new ArgumentException("无效的巢穴发展记录。");
        WorkHours = workHours;
    }

    public void Advance(double seconds, int workers, double meanEnergy, int stored)
    {
        if (!double.IsFinite(seconds) || seconds <= 0 || workers <= 0 || stored <= 2) return;
        double health = Math.Clamp((meanEnergy - 30) / 70, 0, 1);
        double supply = Math.Min(1, (stored - 2) / 10.0);
        WorkHours += seconds / 3600 * workers / 48.0 * health * supply;
    }

    public double RoomsInLayer(long layer)
        => layer == 0 ? 4 : Math.Clamp(CompletedRooms - (layer - 1) * 4.0 + RoomProgress, 0, 4);
}
