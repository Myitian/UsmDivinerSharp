using System.Runtime.InteropServices;

namespace UsmDivinerSharp;

sealed class Vp9ConstraintStats
{
    readonly HashSet<Fingerprint> _conflictFingerprints = [];
    public int AttemptedFrames { get; set; }
    public int MatchedFrames { get; set; }
    public int PlaintextConstraints { get; set; }
    public HashSet<int> Vm1Columns { get; } = [];
    public Dictionary<(string, ConstraintTrust), int> ReasonCounts { get; } = [];
    public Dictionary<string, int> ExtractorCounts { get; } = [];
    public Dictionary<string, int> SameTrustCounts { get; } = [];
    public Dictionary<string, int> CrossTrustCounts { get; } = [];
    // Evidence with trust <= this value is kept for reporting but not used by the solver.
    public ConstraintTrust DisabledTrustThreshold { get; set; }
    public List<Vm1Evidence> Evidences { get; } = [];
    public int ConflictTotal => SameTrustCounts.Values.Sum() + CrossTrustCounts.Values.Sum();
    static string[] FastToNameArray(ConstraintTrust disabledTrustThreshold)
    {
        return disabledTrustThreshold switch
        {
            ConstraintTrust.C9Template => [nameof(ConstraintTrust.C9Template)],
            ConstraintTrust.SingleMarkerExactSize => [nameof(ConstraintTrust.C9Template), nameof(ConstraintTrust.SingleMarkerExactSize)],
            ConstraintTrust.BothMarker => [nameof(ConstraintTrust.C9Template), nameof(ConstraintTrust.SingleMarkerExactSize), nameof(ConstraintTrust.BothMarker)],
            _ => []
        };
    }
    static Dictionary<string, Dictionary<ConstraintTrust, int>> NestedReasonCounts(Dictionary<(string, ConstraintTrust), int> counts)
    {
        Dictionary<string, Dictionary<ConstraintTrust, int>> nested = [];
        foreach (KeyValuePair<(string, ConstraintTrust), int> kvp in counts)
        {
            ref Dictionary<ConstraintTrust, int>? inner = ref CollectionsMarshal.GetValueRefOrAddDefault(nested, kvp.Key.Item1, out _);
            inner ??= [];
            inner[kvp.Key.Item2] = kvp.Value;
        }
        return nested;
    }
    public void AddEvidence(Vm1Evidence evidence, bool countReason = true)
    {
        // Compare with all historical evidence, even if its trust level is disabled,
        // so conflicts remain visible in the report.
        foreach (Vm1Evidence old in Evidences)
        {
            if (old.Column != evidence.Column)
                continue;
            if (old.Values.Overlaps(evidence.Values))
                continue;
            string conflictKey;
            bool isSame = old.Trust == evidence.Trust;
            ConstraintTrust disabledLevel;
            if (isSame)
            {
                conflictKey = old.Trust.FastToString();
                disabledLevel = old.Trust;
            }
            else
            {
                ConstraintTrust low = old.Trust < evidence.Trust ? old.Trust : evidence.Trust;
                ConstraintTrust high = old.Trust > evidence.Trust ? old.Trust : evidence.Trust;
                conflictKey = ConstraintTrust.FastToString(low, high);
                disabledLevel = low;
            }
            Fingerprint fingerprint = new()
            {
                Column = old.Column,
                CounterKey = (isSame, conflictKey),
                Values = evidence.Values.ToSortedArray()
            };
            if (_conflictFingerprints.Add(fingerprint))
                (isSame ? SameTrustCounts : CrossTrustCounts).IncrementCount(conflictKey);
            DisabledTrustThreshold = DisabledTrustThreshold < disabledLevel ? disabledLevel : DisabledTrustThreshold;
        }
        Evidences.Add(evidence);
        Vm1Columns.Add(evidence.Column);
        if (countReason)
        {
            PlaintextConstraints++;
            ReasonCounts.IncrementCount((evidence.Reason, evidence.Trust));
        }
    }
    public void Merge(Vp9ConstraintStats other)
    {
        AttemptedFrames += other.AttemptedFrames;
        MatchedFrames += other.MatchedFrames;
        foreach (KeyValuePair<string, int> kvp in other.ExtractorCounts)
        {
            ref int value = ref CollectionsMarshal.GetValueRefOrAddDefault(ExtractorCounts, kvp.Key, out _);
            value += kvp.Value;
        }
        foreach (Vm1Evidence evidence in other.Evidences)
            AddEvidence(evidence);
    }
    public StatsReport AsReport()
    {
        return new()
        {
            AttemptedFrames = AttemptedFrames,
            MatchedFrames = MatchedFrames,
            PlaintextConstraints = PlaintextConstraints,
            ReasonCounts = NestedReasonCounts(ReasonCounts),
            ExtractorCounts = new(ExtractorCounts),
            ConflictCounts = new()
            {
                SameTrust = new(SameTrustCounts),
                CrossTrust = new(CrossTrustCounts)
            },
            DisabledTrustThreshold = DisabledTrustThreshold,
            DisabledNames = FastToNameArray(DisabledTrustThreshold)
        };
    }
    readonly struct Fingerprint : IEquatable<Fingerprint>
    {
        public int Column { get; init; }
        public (bool IsSame, string ConflictKey) CounterKey { get; init; }
        public int[] Values { get; init; }
        public bool Equals(Fingerprint other)
        {
            return Column == other.Column && CounterKey == other.CounterKey && Values.SequenceEqual(other.Values);
        }
        public override bool Equals(object? obj)
        {
            return obj is Fingerprint other && Equals(other);
        }
        public override int GetHashCode()
        {
            return HashCode.Combine(Column, CounterKey, Values.Length);
        }
    }
}