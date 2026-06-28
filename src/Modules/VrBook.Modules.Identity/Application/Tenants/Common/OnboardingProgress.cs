using VrBook.Contracts.Dtos;

namespace VrBook.Modules.Identity.Application.Tenants.Common;

/// <summary>
/// OPS.M.7 §3.1 + §4.1 — server-side derivation of the onboarding wizard's
/// progress fields. Lives in Identity (the module that owns
/// <c>MeTenantDto</c>'s tenant aggregate fields) so the OPS.M.8 Super Admin
/// view can call into the same logic without going through the wire.
///
/// <para>Pure functions; no DI, no I/O — unit-tested in
/// <c>OnboardingProgressTests</c> with one fact per switch branch so a
/// future state-machine extension fails loudly.</para>
/// </summary>
public static class OnboardingProgress
{
    public const string StepWelcome = "Welcome";
    public const string StepCreateProperty = "CreateProperty";
    public const string StepConnectStripe = "ConnectStripe";
    public const string StepAwaitingVerification = "AwaitingVerification";
    public const string StepDone = "Done";

    public static string DeriveNextStep(MeTenantDto t)
    {
        ArgumentNullException.ThrowIfNull(t);
        return (t.HasStripeAccount, HasAtLeastOneProperty: t.PropertyCount >= 1, t.Status) switch
        {
            (false, false, _) => StepWelcome,
            (false, true, _) => StepConnectStripe,
            (true, false, _) => StepCreateProperty,
            (true, true, "Active") => StepDone,
            (true, true, "Closed") => StepDone,
            (true, true, _) => StepAwaitingVerification,
        };
    }

    public static bool DeriveIsComplete(MeTenantDto t)
    {
        ArgumentNullException.ThrowIfNull(t);
        return t.HasStripeAccount && t.PropertyCount >= 1 && t.Status == "Active";
    }
}
