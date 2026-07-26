using Vm1Constraints = System.Collections.Generic.Dictionary<int, UsmDivinerSharp.Vm1ConstraintEntry>;

namespace UsmDivinerSharp;

public sealed class Vp9Report
{
    public SortedDictionary<string, string[]> Constraints { get; }
    public int ConstraintConflicts { get; }
    public StatsReport ConstraintStats { get; }

    internal Vp9Report(Vm1Constraints constraints, Vp9ConstraintStats stats)
    {
        Constraints = Vp9SuperframeConstraints.FormatVm1Constraints(constraints);
        ConstraintConflicts = stats.ConflictTotal;
        ConstraintStats = stats.AsReport();
    }
}