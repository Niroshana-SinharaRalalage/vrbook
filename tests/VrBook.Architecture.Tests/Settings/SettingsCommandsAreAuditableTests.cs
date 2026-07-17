using FluentAssertions;
using VrBook.Application.Common;
using VrBook.Modules.Admin.Application.Settings;
using Xunit;

namespace VrBook.Architecture.Tests.Settings;

/// <summary>
/// VRB-216/211 (design §5) — every state-changing product-settings command must be
/// <see cref="IAuditable"/> so its change lands in <c>identity.audit_log</c> and the
/// "Recent changes" panel (VRB-211). Guards the convention as more settings commands
/// (VRB-215 per-property, etc.) are added.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SettingsCommandsAreAuditableTests
{
    [Fact]
    public void EverySetSettingsCommand_IsAuditable()
    {
        var settingsNamespace = typeof(SetGlobalTiersCommand).Namespace;
        var writeCommands = typeof(SetGlobalTiersCommand).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                     && t.Namespace == settingsNamespace
                     && t.Name.StartsWith("Set", StringComparison.Ordinal)
                     && t.Name.EndsWith("Command", StringComparison.Ordinal))
            .ToList();

        writeCommands.Should().NotBeEmpty(because: "the VRB-216 settings write commands live in this namespace.");

        var notAuditable = writeCommands
            .Where(t => !typeof(IAuditable).IsAssignableFrom(t))
            .Select(t => t.Name)
            .ToList();

        notAuditable.Should().BeEmpty(
            because: "every Set*Command in the settings namespace must implement IAuditable " +
                     $"(settings.<section>.<verb> audit). Missing: {string.Join(", ", notAuditable)}");
    }
}
