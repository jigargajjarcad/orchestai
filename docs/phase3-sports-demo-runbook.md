# Phase 3 Sports Investigation — Demo Runbook

Prerequisites: Postgres running and migrated (Task 1), seed data loaded (Task 2), `OrchestAI.API`
running locally with a real `Anthropic__ApiKey`.

## 1. Create the task

```
POST /api/v1/tasks
{
  "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "title": "Sports performance investigation",
  "userPrompt": "Investigate Kawhi Leonard's performance over his last 9 games (query the SportsPerformanceGames table for AthleteName = 'Kawhi Leonard', ordered by GameNumber). Independently: (1) analyze the raw performance trend in minutes played and points across these games, and (2) research any publicly reported context (injuries, role changes) around this stretch. Then explicitly compare the two: does the reported context explain the statistical trend, or is there a contradiction between what the numbers show and what the public context suggests? Produce a final conclusion that traces each claim back to whether it came from the statistical data or the contextual research."
}
```

Note: `DevUserId` (`3fa85f64-5717-4562-b3fc-2c963f66afa6`, from `DatabaseSeeder.cs:9`) is the same
well-known dev user ID used throughout this project's demos.

## 2. Start it

```
POST /api/v1/tasks/{id}/start
```

## 3. Read the result

```
GET /api/v1/tasks/{id}
```

Check `finalResult` for the reconciled conclusion, and (optionally) `GET /api/v1/tasks/{id}?includeMessages=true&includeToolCalls=true`
to see each agent's actual `db_query`/`perplexity_search`/`firecrawl_scrape` calls for full traceability.

## What to check on each run

- Did the orchestrator's plan (visible via the `orchestrator_plan` SSE event, or `includeMessages=true`)
  select exactly `Data` and `Research` — not fewer, not more?
- Did `DataAgent` actually call `db_query` against `SportsPerformanceGames` (not hallucinate numbers)?
- Did the final result explicitly address agreement/contradiction between the two sources, citing
  which agent each claim came from?
- If Firecrawl/Perplexity keys are not configured, does `ResearchAgent`'s contribution get
  correctly labeled as unsourced model knowledge rather than presented as cited research?
