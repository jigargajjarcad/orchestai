FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["src/OrchestAI.API/OrchestAI.API.csproj", "src/OrchestAI.API/"]
COPY ["src/OrchestAI.Application/OrchestAI.Application.csproj", "src/OrchestAI.Application/"]
COPY ["src/OrchestAI.Domain/OrchestAI.Domain.csproj", "src/OrchestAI.Domain/"]
COPY ["src/OrchestAI.Infrastructure/OrchestAI.Infrastructure.csproj", "src/OrchestAI.Infrastructure/"]
RUN dotnet restore "src/OrchestAI.API/OrchestAI.API.csproj"

COPY . .
WORKDIR "/src/src/OrchestAI.API"
RUN dotnet publish "OrchestAI.API.csproj" -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "OrchestAI.API.dll"]
