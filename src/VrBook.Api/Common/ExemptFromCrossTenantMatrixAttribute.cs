namespace VrBook.Api.Common;

/// <summary>
/// Slice OPS.M.10 §4.10 (D10) — opt-out marker for the
/// <c>EndpointCoverageArchTest</c>. Apply to a controller (covers every
/// action) or a single action method when the endpoint is intentionally
/// outside the cross-tenant test matrix.
///
/// <para>The <see cref="Reason"/> string is the documentation. Common
/// reasons: <c>"public anonymous endpoint"</c>,
/// <c>"platform-admin own-tenant-only"</c>. Future M.10 maintainers grep
/// the exempt set to verify the reason still holds.</para>
///
/// <para>Adding this attribute is a deliberate review action. The arch
/// test (<c>EndpointCoverageArchTest</c>) fails build if a new
/// authenticated endpoint appears without either an explicit
/// <see cref="ExemptFromCrossTenantMatrixAttribute"/> or a matching
/// row in <c>RouteMatrix.GetAll()</c>.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ExemptFromCrossTenantMatrixAttribute : Attribute
{
    public ExemptFromCrossTenantMatrixAttribute(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Reason = reason;
    }

    public string Reason { get; }
}
