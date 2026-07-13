using Anthropic.SDK;
using Azure.AI.OpenAI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using OrchestAI.Application.Configuration;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Agents;
using OrchestAI.Infrastructure.Agents.Base;
using OrchestAI.Infrastructure.Caching;
using OrchestAI.Infrastructure.Configuration;
using OrchestAI.Infrastructure.Data;
using OrchestAI.Infrastructure.Data.Interceptors;
using OrchestAI.Infrastructure.Eval;
using OrchestAI.Infrastructure.Events;
using OrchestAI.Infrastructure.Observability;
using OrchestAI.Infrastructure.Providers;
using OrchestAI.Infrastructure.Repositories;
using OrchestAI.Infrastructure.Security;
using OrchestAI.Infrastructure.Tenancy;
using OrchestAI.Infrastructure.Tools;
using System.ClientModel;

namespace OrchestAI.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<ICurrentTenantAccessor, AsyncLocalCurrentTenantAccessor>();

        services.AddSingleton<UpdatedAtInterceptor>();
        services.AddSingleton<TenantScopingInterceptor>();

        services.AddDbContextFactory<AppDbContext>((sp, options) =>
        {
            var updatedAtInterceptor = sp.GetRequiredService<UpdatedAtInterceptor>();
            var tenantScopingInterceptor = sp.GetRequiredService<TenantScopingInterceptor>();

            options.UseNpgsql(
                configuration.GetConnectionString("DefaultConnection"),
                npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName));

            options.AddInterceptors(updatedAtInterceptor, tenantScopingInterceptor);
        });

        services.AddScoped<DatabaseSeeder>();

        services.AddScoped<IOrchestrationTaskRepository, OrchestrationTaskRepository>();
        services.AddScoped<IAgentExecutionRepository, AgentExecutionRepository>();
        services.AddScoped<IAgentMessageRepository, AgentMessageRepository>();
        services.AddScoped<ICostLedgerRepository, CostLedgerRepository>();
        services.AddScoped<IMcpToolCallRepository, McpToolCallRepository>();
        services.AddScoped<ITaskCheckpointRepository, TaskCheckpointRepository>();
        services.AddScoped<IAgentMemoryRepository, AgentMemoryRepository>();
        services.AddScoped<IAgentRetryAttemptRepository, AgentRetryAttemptRepository>();
        services.AddScoped<ICostRollupRepository, CostRollupRepository>();
        services.AddScoped<IModelPricingRepository, ModelPricingRepository>();
        services.AddSingleton<IModelPricingCache, ModelPricingCache>();
        services.AddScoped<IEvalSuiteRepository, EvalSuiteRepository>();
        services.AddScoped<IEvalRunRepository, EvalRunRepository>();
        services.AddScoped<IEvalResultRepository, EvalResultRepository>();

        services.AddSingleton<IOrchestrationEventBus, InMemoryOrchestrationEventBus>();
        services.AddSingleton<IApprovalGateway, InMemoryApprovalGateway>();
        services.AddHostedService<CostRollupBackgroundService>();
        // Singleton — the queue is written to by scoped RunEvalSuiteHandler instances and read
        // by the singleton background worker, so both sides need the same instance.
        services.AddSingleton<IEvalRunQueue, InMemoryEvalRunQueue>();
        services.AddHostedService<EvalRunBackgroundWorker>();

        services.Configure<AgentOptions>(configuration.GetSection(AgentOptions.SectionName));
        services.Configure<ToolOptions>(configuration.GetSection(ToolOptions.SectionName));
        services.Configure<RetryPolicyOptions>(configuration.GetSection(RetryPolicyOptions.SectionName));
        services.Configure<PiiRedactionOptions>(configuration.GetSection(PiiRedactionOptions.SectionName));
        services.Configure<EvalOptions>(configuration.GetSection(EvalOptions.SectionName));

        services.AddSingleton<IPiiRedactor, RegexPiiRedactor>();
        services.AddSingleton<IApiKeyHasher, ApiKeyHasher>();

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
        services.AddSingleton<IDatabaseQueryExecutor, AdoDatabaseQueryExecutor>();
        services.AddSingleton<DatabaseTool>();

        services.AddSingleton<IToolRegistry>(sp => new ToolRegistry(new IMcpTool[]
        {
            sp.GetRequiredService<FirecrawlTool>(),
            sp.GetRequiredService<PerplexityTool>(),
            sp.GetRequiredService<FileSystemTool>(),
            sp.GetRequiredService<DatabaseTool>()
        }));

        services.AddScoped<OrchestratorAgent>();
        services.AddScoped<IOrchestratorAgent>(sp => sp.GetRequiredService<OrchestratorAgent>());

        services.AddTransient<ResearchAgent>();
        services.AddTransient<WriterAgent>();
        services.AddTransient<CodeAgent>();
        services.AddTransient<DataAgent>();
        services.AddTransient<BrowserAgent>();

        services.AddScoped<IAgentFactory, AgentFactory>();

        // Scoped, not Singleton (deviates from the plan text): LlmJudgeScorer takes
        // ICostLedgerRepository (Scoped, backed by AppDbContext) directly in its constructor.
        // A Singleton IEvalScorer would capture that scoped dependency, which ASP.NET Core's
        // startup validation correctly rejects. Nothing needs these as Singleton — the only
        // consumer, EvalRunBackgroundWorker, already resolves IEvalScorerFactory from an
        // IServiceScopeFactory-created scope per run (see EvalRunBackgroundWorker.ProcessRunAsync),
        // so Scoped here is safe and correct.
        services.AddScoped<IEvalScorer, RuleBasedScorer>();
        services.AddScoped<IEvalScorer, LlmJudgeScorer>();
        services.AddScoped<IEvalScorerFactory, EvalScorerFactory>();

        return services;
    }
}
