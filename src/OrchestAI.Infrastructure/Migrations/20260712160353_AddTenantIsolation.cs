using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrchestAI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantIsolation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Active"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "NOW()"),
                    SuspendedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApiKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicKeyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    HashedSecret = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "NOW()"),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiKeys", x => x.Id);
                    table.CheckConstraint("CK_ApiKeys_TenantId_NotDefault", "\"TenantId\" <> '00000000-0000-0000-0000-000000000001'");
                    table.ForeignKey(
                        name: "FK_ApiKeys_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Step 1 (of the safe retrofit sequence): seed the default/system tenant. Zero
            // ApiKeys rows are ever created for it — see Tenant.DefaultTenantId and the guard
            // in CreateApiKeyHandler (Task 8).
            migrationBuilder.Sql(
                """
                INSERT INTO "Tenants" ("Id", "Name", "Slug", "Status", "CreatedAt")
                VALUES ('00000000-0000-0000-0000-000000000001', 'Default (Pre-Adoption) Tenant', 'default', 'Active', NOW())
                ON CONFLICT ("Id") DO NOTHING;
                """);

            // Step 2: add TenantId as NULLABLE to all 13 existing tables — safe against
            // already-populated tables, unlike a direct NOT NULL add.
            migrationBuilder.Sql(
                """
                ALTER TABLE "OrchestrationTasks" ADD COLUMN "TenantId" uuid NULL;
                ALTER TABLE "AgentExecutions" ADD COLUMN "TenantId" uuid NULL;
                ALTER TABLE "AgentMemories" ADD COLUMN "TenantId" uuid NULL;
                ALTER TABLE "AgentMessages" ADD COLUMN "TenantId" uuid NULL;
                ALTER TABLE "AgentRetryAttempts" ADD COLUMN "TenantId" uuid NULL;
                ALTER TABLE "CostLedger" ADD COLUMN "TenantId" uuid NULL;
                ALTER TABLE "CostRollups" ADD COLUMN "TenantId" uuid NULL;
                ALTER TABLE "McpToolCalls" ADD COLUMN "TenantId" uuid NULL;
                ALTER TABLE "TaskCheckpoints" ADD COLUMN "TenantId" uuid NULL;
                ALTER TABLE "EvalSuites" ADD COLUMN "TenantId" uuid NULL;
                ALTER TABLE "EvalCases" ADD COLUMN "TenantId" uuid NULL;
                ALTER TABLE "EvalRuns" ADD COLUMN "TenantId" uuid NULL;
                ALTER TABLE "EvalResults" ADD COLUMN "TenantId" uuid NULL;
                """);

            // Step 3: backfill, respecting existing ownership chains — not one independent
            // per-table default assignment. Order matters: parents before children.
            migrationBuilder.Sql(
                """
                UPDATE "OrchestrationTasks" SET "TenantId" = '00000000-0000-0000-0000-000000000001' WHERE "TenantId" IS NULL;
                UPDATE "AgentExecutions" ae SET "TenantId" = ot."TenantId" FROM "OrchestrationTasks" ot WHERE ae."OrchestrationTaskId" = ot."Id" AND ae."TenantId" IS NULL;
                UPDATE "AgentMemories" SET "TenantId" = '00000000-0000-0000-0000-000000000001' WHERE "TenantId" IS NULL;
                UPDATE "AgentMessages" am SET "TenantId" = ae."TenantId" FROM "AgentExecutions" ae WHERE am."AgentExecutionId" = ae."Id" AND am."TenantId" IS NULL;
                UPDATE "AgentRetryAttempts" ara SET "TenantId" = ae."TenantId" FROM "AgentExecutions" ae WHERE ara."AgentExecutionId" = ae."Id" AND ara."TenantId" IS NULL;
                UPDATE "CostLedger" cl SET "TenantId" = ot."TenantId" FROM "OrchestrationTasks" ot WHERE cl."OrchestrationTaskId" = ot."Id" AND cl."TenantId" IS NULL;
                UPDATE "CostRollups" SET "TenantId" = '00000000-0000-0000-0000-000000000001' WHERE "TenantId" IS NULL;
                UPDATE "McpToolCalls" mtc SET "TenantId" = ae."TenantId" FROM "AgentExecutions" ae WHERE mtc."AgentExecutionId" = ae."Id" AND mtc."TenantId" IS NULL;
                UPDATE "TaskCheckpoints" tc SET "TenantId" = ot."TenantId" FROM "OrchestrationTasks" ot WHERE tc."OrchestrationTaskId" = ot."Id" AND tc."TenantId" IS NULL;
                UPDATE "EvalSuites" SET "TenantId" = '00000000-0000-0000-0000-000000000001' WHERE "TenantId" IS NULL;
                UPDATE "EvalCases" ec SET "TenantId" = es."TenantId" FROM "EvalSuites" es WHERE ec."SuiteId" = es."Id" AND ec."TenantId" IS NULL;
                UPDATE "EvalRuns" er SET "TenantId" = es."TenantId" FROM "EvalSuites" es WHERE er."SuiteId" = es."Id" AND er."TenantId" IS NULL;
                UPDATE "EvalRuns" SET "TenantId" = '00000000-0000-0000-0000-000000000001' WHERE "SuiteId" IS NULL AND "TenantId" IS NULL;
                UPDATE "EvalResults" evr SET "TenantId" = er."TenantId" FROM "EvalRuns" er WHERE evr."EvalRunId" = er."Id" AND evr."TenantId" IS NULL;
                """);

            // Step 4 (explicit post-backfill check, not an assumption): fail the migration
            // loudly if any row in any of the 13 tables still has a NULL TenantId — this must
            // never silently proceed to the NOT NULL tightening below with unbackfilled rows.
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    remaining_nulls integer;
                BEGIN
                    SELECT
                        (SELECT count(*) FROM "OrchestrationTasks" WHERE "TenantId" IS NULL) +
                        (SELECT count(*) FROM "AgentExecutions" WHERE "TenantId" IS NULL) +
                        (SELECT count(*) FROM "AgentMemories" WHERE "TenantId" IS NULL) +
                        (SELECT count(*) FROM "AgentMessages" WHERE "TenantId" IS NULL) +
                        (SELECT count(*) FROM "AgentRetryAttempts" WHERE "TenantId" IS NULL) +
                        (SELECT count(*) FROM "CostLedger" WHERE "TenantId" IS NULL) +
                        (SELECT count(*) FROM "CostRollups" WHERE "TenantId" IS NULL) +
                        (SELECT count(*) FROM "McpToolCalls" WHERE "TenantId" IS NULL) +
                        (SELECT count(*) FROM "TaskCheckpoints" WHERE "TenantId" IS NULL) +
                        (SELECT count(*) FROM "EvalSuites" WHERE "TenantId" IS NULL) +
                        (SELECT count(*) FROM "EvalCases" WHERE "TenantId" IS NULL) +
                        (SELECT count(*) FROM "EvalRuns" WHERE "TenantId" IS NULL) +
                        (SELECT count(*) FROM "EvalResults" WHERE "TenantId" IS NULL)
                    INTO remaining_nulls;

                    IF remaining_nulls > 0 THEN
                        RAISE EXCEPTION 'AddTenantIsolation backfill incomplete: % rows still have a NULL TenantId', remaining_nulls;
                    END IF;
                END $$;
                """);

            // Step 5: tighten to NOT NULL now that every row is guaranteed backfilled.
            migrationBuilder.Sql(
                """
                ALTER TABLE "OrchestrationTasks" ALTER COLUMN "TenantId" SET NOT NULL;
                ALTER TABLE "AgentExecutions" ALTER COLUMN "TenantId" SET NOT NULL;
                ALTER TABLE "AgentMemories" ALTER COLUMN "TenantId" SET NOT NULL;
                ALTER TABLE "AgentMessages" ALTER COLUMN "TenantId" SET NOT NULL;
                ALTER TABLE "AgentRetryAttempts" ALTER COLUMN "TenantId" SET NOT NULL;
                ALTER TABLE "CostLedger" ALTER COLUMN "TenantId" SET NOT NULL;
                ALTER TABLE "CostRollups" ALTER COLUMN "TenantId" SET NOT NULL;
                ALTER TABLE "McpToolCalls" ALTER COLUMN "TenantId" SET NOT NULL;
                ALTER TABLE "TaskCheckpoints" ALTER COLUMN "TenantId" SET NOT NULL;
                ALTER TABLE "EvalSuites" ALTER COLUMN "TenantId" SET NOT NULL;
                ALTER TABLE "EvalCases" ALTER COLUMN "TenantId" SET NOT NULL;
                ALTER TABLE "EvalRuns" ALTER COLUMN "TenantId" SET NOT NULL;
                ALTER TABLE "EvalResults" ALTER COLUMN "TenantId" SET NOT NULL;
                """);

            // Step 6: indexes (safe now that every column is NOT NULL and fully populated) — no
            // FK constraint is added here from these 13 tables to "Tenants": Task 2 deliberately
            // configured only Property+HasIndex for TenantId on these 13 entities (no HasOne
            // navigation to Tenant), so dotnet ef migrations add generated zero AddForeignKey
            // calls for them (verified by inspecting the raw generated Up() before this hand
            // edit — only the brand-new ApiKeys->Tenants relationship produced a ForeignKey
            // call, kept above in the CreateTable("ApiKeys", ...) call). These CreateIndex calls
            // are exactly what EF generated for these 13 tables' TenantId columns, moved to this
            // position (after the column is populated and non-null) per this migration's safe
            // retrofit ordering.
            migrationBuilder.CreateIndex(
                name: "IX_TaskCheckpoints_TenantId",
                table: "TaskCheckpoints",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_OrchestrationTasks_TenantId",
                table: "OrchestrationTasks",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_McpToolCalls_TenantId",
                table: "McpToolCalls",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_EvalSuites_TenantId",
                table: "EvalSuites",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_EvalRuns_TenantId",
                table: "EvalRuns",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_EvalResults_TenantId",
                table: "EvalResults",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_EvalCases_TenantId",
                table: "EvalCases",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_CostRollups_TenantId",
                table: "CostRollups",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_CostLedger_TenantId",
                table: "CostLedger",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentRetryAttempts_TenantId",
                table: "AgentRetryAttempts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentMessages_TenantId",
                table: "AgentMessages",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentMemories_TenantId",
                table: "AgentMemories",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentExecutions_TenantId",
                table: "AgentExecutions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_PublicKeyId",
                table: "ApiKeys",
                column: "PublicKeyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_TenantId",
                table: "ApiKeys",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Slug",
                table: "Tenants",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiKeys");

            migrationBuilder.DropTable(
                name: "Tenants");

            migrationBuilder.DropIndex(
                name: "IX_TaskCheckpoints_TenantId",
                table: "TaskCheckpoints");

            migrationBuilder.DropIndex(
                name: "IX_OrchestrationTasks_TenantId",
                table: "OrchestrationTasks");

            migrationBuilder.DropIndex(
                name: "IX_McpToolCalls_TenantId",
                table: "McpToolCalls");

            migrationBuilder.DropIndex(
                name: "IX_EvalSuites_TenantId",
                table: "EvalSuites");

            migrationBuilder.DropIndex(
                name: "IX_EvalRuns_TenantId",
                table: "EvalRuns");

            migrationBuilder.DropIndex(
                name: "IX_EvalResults_TenantId",
                table: "EvalResults");

            migrationBuilder.DropIndex(
                name: "IX_EvalCases_TenantId",
                table: "EvalCases");

            migrationBuilder.DropIndex(
                name: "IX_CostRollups_TenantId",
                table: "CostRollups");

            migrationBuilder.DropIndex(
                name: "IX_CostLedger_TenantId",
                table: "CostLedger");

            migrationBuilder.DropIndex(
                name: "IX_AgentRetryAttempts_TenantId",
                table: "AgentRetryAttempts");

            migrationBuilder.DropIndex(
                name: "IX_AgentMessages_TenantId",
                table: "AgentMessages");

            migrationBuilder.DropIndex(
                name: "IX_AgentMemories_TenantId",
                table: "AgentMemories");

            migrationBuilder.DropIndex(
                name: "IX_AgentExecutions_TenantId",
                table: "AgentExecutions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "TaskCheckpoints");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "OrchestrationTasks");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "McpToolCalls");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "EvalSuites");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "EvalRuns");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "EvalResults");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "EvalCases");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "CostRollups");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "CostLedger");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AgentRetryAttempts");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AgentMessages");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AgentMemories");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AgentExecutions");
        }
    }
}
