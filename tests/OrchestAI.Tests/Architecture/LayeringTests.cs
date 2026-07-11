using FluentAssertions;
using NetArchTest.Rules;

namespace OrchestAI.Tests.Architecture;

// Guardrail against layering violations like the one Week 9 caught only via human review
// (RequestPostHocScoringHandler, in Application, initially depended on EvalOptions in
// Infrastructure.Configuration — a real Clean Architecture violation that no automated check
// would have caught). These tests fail the build the moment a similar violation is introduced,
// instead of waiting for a reviewer to spot it.
public sealed class LayeringTests
{
    private static readonly System.Reflection.Assembly DomainAssembly =
        typeof(OrchestAI.Domain.Entities.EvalRun).Assembly;
    private static readonly System.Reflection.Assembly ApplicationAssembly =
        typeof(OrchestAI.Application.Commands.RequestPostHocScoring.RequestPostHocScoringCommand).Assembly;
    private static readonly System.Reflection.Assembly InfrastructureAssembly =
        typeof(OrchestAI.Infrastructure.Data.AppDbContext).Assembly;
    private static readonly System.Reflection.Assembly ApiAssembly =
        typeof(OrchestAI.API.Controllers.EvalsController).Assembly;

    [Fact]
    public void Domain_DoesNotDependOnAnyOtherLayer()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOnAny("OrchestAI.Application", "OrchestAI.Infrastructure", "OrchestAI.API")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            FailureDetail(result, "Domain must not reference Application, Infrastructure, or API"));
    }

    [Fact]
    public void Application_DoesNotDependOnInfrastructure()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOn("OrchestAI.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            FailureDetail(result, "Application must not reference Infrastructure"));
    }

    [Fact]
    public void Application_DoesNotDependOnApi()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOn("OrchestAI.API")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            FailureDetail(result, "Application must not reference API"));
    }

    [Fact]
    public void Infrastructure_DoesNotDependOnApi()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .Should()
            .NotHaveDependencyOn("OrchestAI.API")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            FailureDetail(result, "Infrastructure must not reference API"));
    }

    // Confirms the layers actually used above resolve to distinct assemblies (Domain,
    // Application, Infrastructure, API) — if a future refactor merged two layers into one
    // assembly, the dependency checks above would trivially "pass" by having nothing to find,
    // silently disabling this guardrail. This keeps that failure mode loud instead of silent.
    [Fact]
    public void FourLayerAssemblies_AreAllDistinct()
    {
        var assemblies = new[] { DomainAssembly, ApplicationAssembly, InfrastructureAssembly, ApiAssembly };
        assemblies.Distinct().Should().HaveCount(4, "each layer must compile to its own assembly for these rules to mean anything");
    }

    private static string FailureDetail(NetArchTest.Rules.TestResult result, string ruleDescription) =>
        $"{ruleDescription}. Violating types: {string.Join(", ", result.FailingTypeNames ?? [])}";
}
