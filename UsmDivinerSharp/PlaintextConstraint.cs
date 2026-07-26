namespace UsmDivinerSharp;

readonly record struct PlaintextConstraint(
    int PayloadOffset,
    int AllowedValue,
    string Reason);