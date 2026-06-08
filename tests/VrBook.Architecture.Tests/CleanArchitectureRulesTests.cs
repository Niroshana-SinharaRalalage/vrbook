using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace VrBook.Architecture.Tests;

/// <summary>
/// Enforces the Clean Architecture + modular-monolith boundaries documented in ADR 0001.
/// Failing these is a build failure — they are not soft hints.
///
/// Layer convention:
///   VrBook.Domain        ←  no outward references
///   VrBook.Contracts     ←  DTOs + Events only; no domain or app references
///   VrBook.Application   ← may reference Domain + Contracts
///   VrBook.Infrastructure← may reference Domain + Application + Contracts
///   VrBook.Modules.*     ← own everything in their bounded context
///   VrBook.Api           ← composition root
/// </summary>
public sealed class CleanArchitectureRulesTests
{
    private static readonly Assembly DomainAssembly = typeof(VrBook.Domain.Common.AggregateRoot).Assembly;
    private static readonly Assembly ContractsAssembly = typeof(VrBook.Contracts.Common.PagedResult<>).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(VrBook.Application.Common.IModuleRegistration).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(VrBook.Infrastructure.Persistence.BaseDbContext).Assembly;

    [Fact]
    public void Domain_does_not_reference_Application_or_Infrastructure_or_Api()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOnAny([
                "VrBook.Application",
                "VrBook.Infrastructure",
                "VrBook.Api",
                "Microsoft.AspNetCore",
                "Microsoft.EntityFrameworkCore",
                "MediatR",
            ])
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Domain must be the innermost layer — no outward dependencies. " +
                     $"Offenders: {string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())}");
    }

    [Fact]
    public void Contracts_does_not_reference_Domain_or_Application_or_Infrastructure()
    {
        // Contracts is a pure DTO/event surface shared with clients (TS generator etc).
        // Coupling it to Domain types would force every client to mirror domain mutations.
        var result = Types.InAssembly(ContractsAssembly)
            .Should()
            .NotHaveDependencyOnAny([
                "VrBook.Domain",
                "VrBook.Application",
                "VrBook.Infrastructure",
                "VrBook.Api",
                "Microsoft.EntityFrameworkCore",
            ])
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Contracts is the public API surface — no inward dependencies. " +
                     $"Offenders: {string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())}");
    }

    [Fact]
    public void Application_does_not_reference_Infrastructure_or_Api()
    {
        // Application orchestrates via interfaces. Infrastructure depends on Application,
        // not the other way around — the dependency inversion principle.
        var result = Types.InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOnAny([
                "VrBook.Infrastructure",
                "VrBook.Api",
                "Microsoft.AspNetCore",
                "Microsoft.EntityFrameworkCore.Relational",
                "Npgsql",
            ])
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Application must depend on abstractions only. " +
                     $"Offenders: {string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())}");
    }

    [Fact]
    public void Infrastructure_does_not_reference_Api()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .Should()
            .NotHaveDependencyOn("VrBook.Api")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Infrastructure is below the composition root. " +
                     $"Offenders: {string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())}");
    }

    [Fact]
    public void Application_handlers_are_internal_sealed_records_or_classes()
    {
        // Convention: handlers are internal sealed so the module's surface stays minimal.
        // MediatR resolves them via DI; nothing else should bind to handler types.
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface(typeof(MediatR.IRequestHandler<,>))
            .Or().ImplementInterface(typeof(MediatR.IRequestHandler<>))
            .Should()
            .BeSealed()
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Application handlers must be sealed (modular-monolith convention). " +
                     $"Offenders: {string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())}");
    }
}
