using Microsoft.EntityFrameworkCore;
using OrchestAI.API.ExceptionHandling;
using OrchestAI.API.Filters;
using OrchestAI.API.Middleware;
using OrchestAI.API.RateLimiting;
using OrchestAI.Application;
using OrchestAI.Infrastructure;
using OrchestAI.Infrastructure.Data;
using Serilog;
using Serilog.Formatting.Compact;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting OrchestAI API");

    var builder = WebApplication.CreateBuilder(args);

    // Railway sets $PORT — bind to it; fall back to 8080 for Docker/local
    var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
    builder.WebHost.UseUrls($"http://+:{port}");

    builder.Host.UseSerilog((ctx, services, config) =>
        config.ReadFrom.Configuration(ctx.Configuration)
              .ReadFrom.Services(services)
              .Enrich.FromLogContext()
              .WriteTo.Console(new CompactJsonFormatter()));

    builder.Services.AddControllers()
        .AddJsonOptions(options =>
            options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

    // Gates AdminController via [ServiceFilter(typeof(RequireAdminSecretFilter))] — registered
    // here rather than in AddInfrastructure because the filter is MVC-specific glue (API layer),
    // not persistence/cross-cutting infrastructure.
    builder.Services.AddScoped<RequireAdminSecretFilter>();

    // IMiddleware-based custom middleware must be registered in DI (UseMiddleware<T>() resolves
    // it via IMiddlewareFactory) — registered here, not in AddInfrastructure, for the same reason
    // as RequireAdminSecretFilter above: this is ASP.NET Core HTTP-pipeline glue (API layer), not
    // persistence/cross-cutting infrastructure. See Task 9 / ADR-014.
    builder.Services.AddScoped<TenantAuthenticationMiddleware>();

    // API-layer rejection contract (Task 2) — RejectionResponder builds every 429 response and
    // RejectionEvent write; TenantLimitExceededExceptionHandler is the choke point for every
    // synchronous-HTTP-request rejection thrown as an exception.
    builder.Services.AddScoped<RejectionResponder>();
    builder.Services.AddExceptionHandler<TenantLimitExceededExceptionHandler>();
    builder.Services.AddTenantRateLimiting();

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new()
        {
            Title = "OrchestAI API",
            Version = "v1",
            Description = "Production-ready multi-agent AI orchestration for .NET 8"
        });

        var xmlFile = $"{typeof(Program).Assembly.GetName().Name}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        if (File.Exists(xmlPath))
            options.IncludeXmlComments(xmlPath);
    });

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);

    var allowedOrigins = (builder.Configuration["ALLOWED_ORIGINS"]
        ?? "http://localhost:3000,http://localhost:5173")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    builder.Services.AddCors(options =>
        options.AddPolicy("Frontend", policy =>
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()));

    builder.Services.AddProblemDetails();

    var app = builder.Build();

    // Auto-migrate on startup (safe for single-instance Railway deployments)
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync();

        var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
        await seeder.SeedAsync();
    }

    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "OrchestAI API v1"));
    }

    app.UseExceptionHandler();
    app.UseStatusCodePages();
    app.UseCors("Frontend");
    // The tenant auth gate: resolves the Bearer API key to a tenant and sets the ambient
    // ICurrentTenantAccessor scope for every downstream handler. Exempts /health, /swagger, and
    // /api/v1/admin/* (the admin-bootstrap surface, gated separately by RequireAdminSecretFilter).
    // Fail-closed: missing/malformed/unknown/revoked key -> 401; valid key, suspended tenant -> 403.
    app.UseMiddleware<TenantAuthenticationMiddleware>();
    app.UseRateLimiter();
    app.MapControllers();
    app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTimeOffset.UtcNow }));

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "OrchestAI API terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
