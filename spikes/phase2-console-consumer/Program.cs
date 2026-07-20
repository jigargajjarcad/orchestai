using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrchestAI.Application;
using OrchestAI.Application.Commands.AdmitOrchestrationTask;
using OrchestAI.Application.Commands.CreateOrchestrationTask;
using OrchestAI.Application.Commands.StartOrchestration;
using OrchestAI.Application.Queries.GetOrchestrationTask;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure;
using OrchestAI.Infrastructure.Data;

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables()
    .Build();

var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
services.AddApplication();
services.AddInfrastructure(configuration);

await using var provider = services.BuildServiceProvider();

// AddInfrastructure() only registers IDbContextFactory<AppDbContext> — nothing runs
// migrations automatically outside the ASP.NET Core host's own Program.cs startup block.
// A library consumer owns this step exactly like OrchestAI.API's Program.cs does.
await using (var scope = provider.CreateAsyncScope())
{
    var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    await using var dbContext = await dbContextFactory.CreateDbContextAsync();
    await dbContext.Database.MigrateAsync();
}

// No HTTP request exists to resolve a tenant from — this is the direct, non-HTTP
// equivalent of what TenantAuthenticationMiddleware does per-request.
var tenantAccessor = provider.GetRequiredService<ICurrentTenantAccessor>();
using var tenantScope = tenantAccessor.SetTenant(Tenant.DefaultTenantId);

await using var requestScope = provider.CreateAsyncScope();
var mediator = requestScope.ServiceProvider.GetRequiredService<IMediator>();

Console.WriteLine("Submitting task...");
var created = await mediator.Send(new CreateOrchestrationTaskCommand(
    UserId: DatabaseSeeder.DevUserId,
    Title: "Phase 2 packaging spike",
    UserPrompt: "In one sentence, what is the capital of France?"));

Console.WriteLine($"Created task {created.Id}, status {created.Status}");

// Admission is not optional here — StartOrchestrationCommand throws InvalidOperationException
// on a task still in Pending. This is the Application-layer state machine from ADR-015,
// unrelated to and unbypassable via the HTTP rate limiter, which this process never touches.
await mediator.Send(new AdmitOrchestrationTaskCommand(created.Id));
Console.WriteLine("Admitted. Running orchestration synchronously (no HTTP, no SSE, no fire-and-forget)...");

await mediator.Send(new StartOrchestrationCommand(created.Id));

var result = await mediator.Send(new GetOrchestrationTaskQuery(created.Id));

Console.WriteLine();
Console.WriteLine($"Final status: {result!.Status}");
Console.WriteLine($"Result: {result.FinalResult}");
Console.WriteLine($"Cost: ${result.TotalCostUsd:F4} — tokens in/out: {result.TotalInputTokens}/{result.TotalOutputTokens}");

if (result.Status != "Completed")
{
    Console.WriteLine($"Error: {result.ErrorMessage}");
    Environment.Exit(1);
}
