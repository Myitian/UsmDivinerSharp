namespace UsmDivinerSharp;

public enum ConstraintTrust
{
    None,
    /// <summary>
    /// Empirical C9 template for tiny files; useful but not a VP9 spec rule.
    /// </summary>
    C9Template,
    /// <summary>
    /// High-trust structural evidence: one observed marker plus exact observed sizes.
    /// </summary>
    SingleMarkerExactSize,
    /// <summary>
    /// Engineering-trusted: both superframe markers are independently keyless-readable.
    /// </summary>
    BothMarker
}