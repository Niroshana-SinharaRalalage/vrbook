using System.Reflection;
using FluentAssertions;
using VrBook.Contracts.Interfaces;
using Xunit;

namespace VrBook.Architecture.Tests;

/// <summary>
/// Slice OPS.M.14.3 — locks the <c>ICurrentUser.B2CObjectId</c> →
/// <c>ExternalObjectId</c> rename. "B2C" was the pre-Entra provider brand;
/// ADR-0012 pinned identity to Entra External ID, and OPS.M.12 will add
/// social IdPs behind the same <c>oid</c> claim. The property must describe
/// what it IS (the identity provider's object id) rather than which provider
/// happened to issue it in 2025.
/// </summary>
[Trait("Category", "Unit")]
public sealed class OpsM14_ExternalObjectIdRenameTests
{
    [Fact]
    public void ICurrentUser_has_no_B2CObjectId_member()
    {
        typeof(ICurrentUser).GetProperty("B2CObjectId").Should().BeNull(
            because: "the pre-M.14 property name is retired; a reintroduction would fork the identifier + rot the abstraction.");
    }

    [Fact]
    public void ICurrentUser_exposes_ExternalObjectId()
    {
        var prop = typeof(ICurrentUser).GetProperty(
            "ExternalObjectId", BindingFlags.Public | BindingFlags.Instance);
        prop.Should().NotBeNull();
        prop!.PropertyType.Should().Be(typeof(string));
    }

    [Fact]
    public void No_src_source_file_contains_identifier_B2CObjectId()
    {
        var root = RepoRoot();
        var srcRoot = Path.Combine(root, "src");
        Directory.Exists(srcRoot).Should().BeTrue(srcRoot);

        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            if (name.EndsWith(".Designer.cs", StringComparison.Ordinal))
            {
                continue;
            }
            if (name.EndsWith("ModelSnapshot.cs", StringComparison.Ordinal))
            {
                continue;
            }
            var text = File.ReadAllText(file);
            if (text.Contains("B2CObjectId", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetRelativePath(root, file));
            }
        }
        offenders.Should().BeEmpty(
            because: "the identifier is gone from live source; EF migration Designer + ModelSnapshot files are exempt because that history is immutable.\n" +
                     "Offenders:\n" + string.Join("\n", offenders));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull(
            because: "the test must run from inside the repo so it can scan source files.");
        return dir!.FullName;
    }
}
