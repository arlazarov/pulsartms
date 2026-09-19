namespace API;

// Opt in only for operational data endpoints that never return authentication
// secrets.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class CompressResponseAttribute : Attribute;
