using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrchestAI.Application;
using OrchestAI.Infrastructure;

namespace OrchestAI.Tests.Infrastructure;

// Closes ADR-017 Confirmation #7b: a bare ServiceCollection consumer (e.g. the Phase 2 disposable
// console app) had no way to resolve IConfiguration after calling AddApplication()/
// AddInfrastructure() alone, because nothing in AddInfrastructure registered it and ASP.NET
// Core's own host was the only thing that ever did. These tests drive the real
// DependencyInjection.AddInfrastructure extension method directly (not a mock of it) against both
// shapes: a bare ServiceCollection (the gap this fix closes) and a ServiceCollection that already
// has IConfiguration registered first (the real ASP.NET Core host's own shape), proving the fix
// helps the first case without double-registering or replacing the instance in the second.
public sealed class DependencyInjectionTests
{
    private static IConfiguration BuildValidConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Port=5432;Database=orchestai;Username=orchestai;Password=changeme",
            ["Anthropic:ApiKey"] = "sk-ant-real-key"
        }).Build();

    [Fact]
    public void AddInfrastructure_BareServiceCollection_RegistersConfigurationResolvableWithoutManualRegistration()
    {
        // Mirrors the Phase 2 disposable console consumer's exact composition root shape: a plain
        // `new ServiceCollection()`, no host, no prior IConfiguration registration of any kind.
        var services = new ServiceCollection();
        var configuration = BuildValidConfig();

        services.AddApplication();
        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetService<IConfiguration>();

        resolved.Should().NotBeNull(
            "a bare ServiceCollection consumer must be able to resolve IConfiguration after " +
            "AddInfrastructure alone, without registering it themselves first");
        resolved.Should().BeSameAs(configuration,
            "the registered instance should be the exact configuration object AddInfrastructure was given");
    }

    [Fact]
    public void AddInfrastructure_HostAlreadyRegisteredConfiguration_DoesNotDuplicateOrReplaceIt()
    {
        // Mirrors what WebApplicationBuilder already does before Program.cs's own
        // AddInfrastructure(builder.Configuration) call ever runs: IConfiguration is registered
        // by the host itself, first. TryAddSingleton must no-op here, not add a second descriptor.
        var services = new ServiceCollection();
        var configuration = BuildValidConfig();

        services.AddSingleton(configuration); // simulates the ASP.NET Core host's own registration

        services.AddApplication();
        services.AddInfrastructure(configuration);

        var configurationDescriptors = services
            .Where(d => d.ServiceType == typeof(IConfiguration))
            .ToList();

        configurationDescriptors.Should().ContainSingle(
            "AddInfrastructure must not add a second IConfiguration registration when the host " +
            "(e.g. ASP.NET Core) already registered one");

        using var provider = services.BuildServiceProvider();
        provider.GetService<IConfiguration>().Should().BeSameAs(configuration,
            "the pre-existing host registration must remain the one resolved, not be silently replaced");
    }
}
