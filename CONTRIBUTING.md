# Contributing

## Local setup

See the [Quick Start](README.md#quick-start-local) in the README.

Run the test suite before opening a PR:

```bash
dotnet test tests/OrchestAI.Tests
```

## Architecture decisions

Non-obvious design decisions are logged as ADRs in [`DECISIONS.md`](DECISIONS.md). Read it before changing anything in the observability (`OBSERVABILITY.md`) or eval/scoring layers — several schema and lifetime choices there look arbitrary until you see the reasoning.

## Gotchas

- **`CreatedAtAction`/`AcceptedAtAction` need the routed action name, not `nameof(MethodNameAsync)`.** ASP.NET Core strips the `Async` suffix from action names by default (`SuppressAsyncSuffixInActionNames`), so `nameof(GetByIdAsync)` produces the string `"GetByIdAsync"`, which never matches the actual routed action name `"GetById"` — the call throws `InvalidOperationException: No route matches the supplied values` at runtime, even though a `dotnet build` and a normal code review won't catch it, and even though the underlying command still executes and persists data. Use the literal trimmed string instead (see `TasksController.CreateAsync`'s `CreatedAtAction("GetById", ...)` for the pattern). Verify any new use of these helpers with a real HTTP call, not just a build.
- **There is no global exception-handling middleware.** `Program.cs` registers `AddProblemDetails()` + `UseExceptionHandler()`, but no custom `IExceptionHandler`. Every controller that needs to turn a domain exception (`NotFoundException`, `ValidationException`) into a specific HTTP status must catch it locally, per action — see `TasksController`/`MemoriesController`/`EvalsController` for the established `catch (NotFoundException ex) => NotFound(new ProblemDetails { ... })` pattern. An uncaught domain exception falls through to a generic 500.
