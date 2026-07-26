namespace UsmDivinerSharp;

sealed record class Vm1Evidence(
    int Column,
    HashSet<int> Values,
    ConstraintTrust Trust,
    string Reason);