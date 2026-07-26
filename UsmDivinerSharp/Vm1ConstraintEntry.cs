namespace UsmDivinerSharp;

sealed record class Vm1ConstraintEntry(
    HashSet<int> Values,
    ConstraintTrust Trust,
    string Reason,
    int Support = 1);