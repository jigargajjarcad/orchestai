using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrchestAI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSportsPerformanceGamesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SportsPerformanceGames is intentionally shared, non-tenant-scoped reference data —
            // same pattern as the existing ModelPricing table (see DatabaseSeeder.cs) — queried
            // entirely through the generic db_query/DatabaseTool tool, never via EF Core, so it
            // has no ITenantScoped/TenantId column by design, not by oversight.
            migrationBuilder.Sql("""
                CREATE TABLE "SportsPerformanceGames" (
                    "Id" SERIAL PRIMARY KEY,
                    "AthleteName" TEXT NOT NULL,
                    "Sport" TEXT NOT NULL,
                    "GameNumber" INT NOT NULL,
                    "GameDate" DATE NOT NULL,
                    "Opponent" TEXT NOT NULL,
                    "IsHomeGame" BOOLEAN NOT NULL,
                    "MinutesPlayed" INT NOT NULL,
                    "Points" INT NOT NULL,
                    "ReportedInjuryNote" TEXT NULL,
                    "SourceUrl" TEXT NULL
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP TABLE "SportsPerformanceGames";""");
        }
    }
}
