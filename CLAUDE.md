# OrchestAI — Project Context for Claude Code

## What This Project Is
Production-ready C# .NET 8 boilerplate for building multi-agent AI 
orchestration systems. Fills the gap in the .NET ecosystem — Python has 
LangGraph/AutoGen/CrewAI, .NET has nothing. OrchestAI changes that.

## Tech Stack
- Backend: C# .NET 8, ASP.NET Core, MediatR (CQRS)
- AI: Anthropic Claude API (claude-sonnet-4-6), Anthropic .NET SDK
- MCP: Custom IMcpTool implementation
- ORM: Entity Framework Core 8 (async)
- Database: PostgreSQL 15
- Streaming: Server-Sent Events (SSE)
- Frontend: React, TailwindCSS, React Query
- Containerization: Docker + Docker Compose
- Deployment: Railway (API + DB), Vercel (Frontend)

## Project Structure
- /backend — .NET solution (OrchestAI.sln)
  - OrchestAI.API — ASP.NET Core controllers and SSE
  - OrchestAI.Application — CQRS commands, queries, handlers, DTOs
  - OrchestAI.Domain — Entities, interfaces, enums
  - OrchestAI.Infrastructure — Agents, MCP tools, EF Core, repositories
- /frontend — React application
- /PRD.md — Full product requirements (read this for complete context)

## Architecture
Multi-agent orchestration:
User Task → API → CQRS Command → OrchestratorAgent → 
[ResearchAgent + CodeAgent + DataAgent] parallel → 
MCP Tools → Aggregate → SSE Stream → React UI

## Agents
- OrchestratorAgent: Decomposes tasks, assigns to specialists, aggregates
- ResearchAgent: Web research via WebSearchTool
- CodeAgent: Code generation via FileSystemTool
- DataAgent: Data analysis via DatabaseTool

## Coding Standards — Non-Negotiable
- Controllers handle HTTP only — all logic in MediatR handlers
- CQRS for everything — commands mutate, queries read
- IAgent and IMcpTool interfaces — never concrete dependencies
- Always async/await with CancellationToken throughout
- Structured logging on all agent operations
- Repository pattern for all DB access
- Custom exception types with error codes
- Never .Result or .Wait() — always await

## Current Phase
Week 1 — Foundation
- Docker Compose with PostgreSQL
- .NET solution structure (4 projects)
- EF Core with all tables + migrations
- MediatR with first command/query
- Basic API skeleton

## Key Differentiator
First production-grade multi-agent CQRS framework for .NET.
This is open source — code quality, README, and architecture 
decisions represent the .NET AI engineering community standard.
