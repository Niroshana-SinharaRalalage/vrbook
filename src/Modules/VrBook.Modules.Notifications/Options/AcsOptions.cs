namespace VrBook.Modules.Notifications.Options;

/// <summary>
/// Bound from configuration section <c>Acs</c> (VRB-200). Azure Communication
/// Services Email connection + sender identity. When <see cref="ConnectionString"/>
/// is empty the notifications pipeline runs email-disabled (no dispatch), so an
/// empty section is valid in every environment; a non-empty connection string
/// requires a valid <see cref="SenderAddress"/> (fail-fast validated).
/// </summary>
public sealed class AcsOptions
{
    public const string SectionName = "Acs";

    public string ConnectionString { get; set; } = string.Empty;

    public string SenderAddress { get; set; } = string.Empty;
}
