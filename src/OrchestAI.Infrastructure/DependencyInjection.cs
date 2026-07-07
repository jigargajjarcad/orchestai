using Anthropic.SDK;
using Azure.AI.OpenAI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Agents;
using OrchestAI.Infrastructure.Agents.Base;
using OrchestAI.Infrastructure.Configuration;
using OrchestAI.Infrastructure.Data;
using OrchestAI.Infrastructure.Data.Interceptors;
using OrchestAI.Infrastructure.Events;
using OrchestAI.Infrastructure.Providers;
using OrchestAI.Infrastructure.Repositories;
using OrchestAI.Infrastructure.Tools;
using System.ClientModel;

namespace OrchestAI.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<UpdatedAtInterceptor>();

        services.AddDbContextFactory<AppDbContext>((sp, options) =>
        {
            var interceptor = sp.GetRequiredService<UpdatedAtInterceptor>();

            options.UseNpgsql(
                configuration.GetConnectionString("DefaultConnection"),
                npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName));

            options.AddInterceptors(interceptor);
        });

        services.AddScoped<DatabaseSeeder>();

        services.AddScoped<IOrchestrationTaskRepository, OrchestrationTaskRepository>();
        services.AddScoped<IAgentExecutionRepository, AgentExecutionRepository>();
        services.AddScoped<IAgentMessageRepository, AgentMessageRepository>();
        services.AddScoped<ICostLedgerRepository, CostLedgerRepository>();
        services.AddScoped<IMcpToolCallRepository, McpToolCallRepository>();

        services.AddSingleton<IOrchestrationEventBus, InMemoryOrchestrationEventBus>();
        services.AddSingleton<IApprovalGateway, InMemoryApprovalGateway>();

        services.Configure<AgentOptions>(configuration.GetSection(AgentOptions.SectionName));
        services.Configure<Dictionary<string, PricingEntry>>(configuration.GetSection("Pricing"));
        services.Configure<ToolOptions>(configuration.GetSection(ToolOptions.SectionName));

        var apiKey = configuration["Anthropic:ApiKey"]
            ?? throw new InvalidOperationException(
                "Anthropic:ApiKey is not configured. Set the Anthropic__ApiKey environment variable.");

        services.AddSingleton(new AnthropicClient(new APIAuthentication(apiKey)));
        services.AddSingleton<IAnthropicClientWrapper, AnthropicClientWrapper>();

        services.AddSingleton<ILlmProvider>(sp =>
            new AnthropicProvider(sp.GetRequiredService<IAnthropicClientWrapper>()));

        var azureApiKey = configuration["AzureOpenAI:ApiKey"];
        var azureEndpoint = configuration["AzureOpenAI:Endpoint"];
        var azureDeploymentName = configuration["AzureOpenAI:DeploymentName"];
        if (!string.IsNullOrWhiteSpace(azureApiKey)
            && !string.IsNullOrWhiteSpace(azureEndpoint)
            && !string.IsNullOrWhiteSpace(azureDeploymentName))
        {
            var azureClient = new AzureOpenAIClient(new Uri(azureEndpoint), new ApiKeyCredential(azureApiKey));
            services.AddSingleton<ILlmProvider>(
                new AzureOpenAIProvider(new AzureOpenAIChatCompletionClient(azureClient, azureDeploymentName)));
        }

        var openAiApiKey = configuration["OpenAI:ApiKey"];
        if (!string.IsNullOrWhiteSpace(openAiApiKey))
        {
            var openAiClient = new OpenAIClient(openAiApiKey);
            services.AddSingleton<ILlmProvider>(new OpenAIProvider(new OpenAIChatCompletionClient(openAiClient)));
        }

        services.AddSingleton<ILlmProviderFactory, LlmProviderFactory>();

        // Tools — use IHttpClientFactory so singletons can create scoped HTTP clients safely
        services.AddHttpClient();
        services.AddSingleton<FirecrawlTool>();
        services.AddSingleton<PerplexityTool>();
        services.AddSingleton<FileSystemTool>();

        services.AddSingleton<IToolRegistry>(sp => new ToolRegistry(new IMcpTool[]
        {
            sp.GetRequiredService<FirecrawlTool>(),
            sp.GetRequiredService<PerplexityTool>(),
            sp.GetRequiredService<FileSystemTool>()
        }));

        services.AddScoped<OrchestratorAgent>();
        services.AddScoped<IOrchestratorAgent>(sp => sp.GetRequiredService<OrchestratorAgent>());

        services.AddTransient<ResearchAgent>();
        services.AddTransient<WriterAgent>();
        services.AddTransient<CodeAgent>();
        services.AddTransient<DataAgent>();
        services.AddTransient<BrowserAgent>();

        services.AddScoped<IAgentFactory, AgentFactory>();

        return services;
    }
}
