using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrchestAI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddObservabilityFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ResumedAt",
                table: "OrchestrationTasks",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TraceId",
                table: "OrchestrationTasks",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValueSql: "replace(gen_random_uuid()::text, '-', '')");

            migrationBuilder.AddColumn<string>(
                name: "ErrorCategory",
                table: "McpToolCalls",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParentSpanId",
                table: "McpToolCalls",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValueSql: "substring(replace(gen_random_uuid()::text, '-', ''), 1, 16)");

            migrationBuilder.AddColumn<string>(
                name: "SpanId",
                table: "McpToolCalls",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValueSql: "substring(replace(gen_random_uuid()::text, '-', ''), 1, 16)");

            migrationBuilder.AddColumn<string>(
                name: "ErrorCategory",
                table: "AgentExecutions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MemoriesInjectedCount",
                table: "AgentExecutions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ParentSpanId",
                table: "AgentExecutions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpanId",
                table: "AgentExecutions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValueSql: "substring(replace(gen_random_uuid()::text, '-', ''), 1, 16)");

            migrationBuilder.CreateTable(
                name: "AgentRetryAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    AgentExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    DelayMs = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentRetryAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentRetryAttempts_AgentExecutions_AgentExecutionId",
                        column: x => x.AgentExecutionId,
                        principalTable: "AgentExecutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CostRollups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    InputTokens = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CostUsd = table.Column<decimal>(type: "numeric(10,6)", nullable: false, defaultValue: 0m),
                    ExecutionCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostRollups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ModelPricing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    InputPerMillion = table.Column<decimal>(type: "numeric(10,4)", nullable: false),
                    OutputPerMillion = table.Column<decimal>(type: "numeric(10,4)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelPricing", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrchestrationTasks_TraceId",
                table: "OrchestrationTasks",
                column: "TraceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_McpToolCalls_ParentSpanId",
                table: "McpToolCalls",
                column: "ParentSpanId");

            migrationBuilder.CreateIndex(
                name: "IX_McpToolCalls_SpanId",
                table: "McpToolCalls",
                column: "SpanId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_McpToolCalls_Success_ErrorCategory",
                table: "McpToolCalls",
                columns: new[] { "Success", "ErrorCategory" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentExecutions_AgentType_CreatedAt",
                table: "AgentExecutions",
                columns: new[] { "AgentType", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentExecutions_ParentSpanId",
                table: "AgentExecutions",
                column: "ParentSpanId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentExecutions_SpanId",
                table: "AgentExecutions",
                column: "SpanId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentExecutions_Status_ErrorCategory",
                table: "AgentExecutions",
                columns: new[] { "Status", "ErrorCategory" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentRetryAttempts_AgentExecutionId",
                table: "AgentRetryAttempts",
                column: "AgentExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_CostRollups_Date_UserId_AgentType_Model",
                table: "CostRollups",
                columns: new[] { "Date", "UserId", "AgentType", "Model" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CostRollups_UserId_Date",
                table: "CostRollups",
                columns: new[] { "UserId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_ModelPricing_Model",
                table: "ModelPricing",
                column: "Model",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentRetryAttempts");

            migrationBuilder.DropTable(
                name: "CostRollups");

            migrationBuilder.DropTable(
                name: "ModelPricing");

            migrationBuilder.DropIndex(
                name: "IX_OrchestrationTasks_TraceId",
                table: "OrchestrationTasks");

            migrationBuilder.DropIndex(
                name: "IX_McpToolCalls_ParentSpanId",
                table: "McpToolCalls");

            migrationBuilder.DropIndex(
                name: "IX_McpToolCalls_SpanId",
                table: "McpToolCalls");

            migrationBuilder.DropIndex(
                name: "IX_McpToolCalls_Success_ErrorCategory",
                table: "McpToolCalls");

            migrationBuilder.DropIndex(
                name: "IX_AgentExecutions_AgentType_CreatedAt",
                table: "AgentExecutions");

            migrationBuilder.DropIndex(
                name: "IX_AgentExecutions_ParentSpanId",
                table: "AgentExecutions");

            migrationBuilder.DropIndex(
                name: "IX_AgentExecutions_SpanId",
                table: "AgentExecutions");

            migrationBuilder.DropIndex(
                name: "IX_AgentExecutions_Status_ErrorCategory",
                table: "AgentExecutions");

            migrationBuilder.DropColumn(
                name: "ResumedAt",
                table: "OrchestrationTasks");

            migrationBuilder.DropColumn(
                name: "TraceId",
                table: "OrchestrationTasks");

            migrationBuilder.DropColumn(
                name: "ErrorCategory",
                table: "McpToolCalls");

            migrationBuilder.DropColumn(
                name: "ParentSpanId",
                table: "McpToolCalls");

            migrationBuilder.DropColumn(
                name: "SpanId",
                table: "McpToolCalls");

            migrationBuilder.DropColumn(
                name: "ErrorCategory",
                table: "AgentExecutions");

            migrationBuilder.DropColumn(
                name: "MemoriesInjectedCount",
                table: "AgentExecutions");

            migrationBuilder.DropColumn(
                name: "ParentSpanId",
                table: "AgentExecutions");

            migrationBuilder.DropColumn(
                name: "SpanId",
                table: "AgentExecutions");
        }
    }
}
