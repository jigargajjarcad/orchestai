using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrchestAI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEvalScoringModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EvalRunId",
                table: "CostLedger",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "CostLedger",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Production");

            migrationBuilder.AddColumn<Guid>(
                name: "EvalRunId",
                table: "AgentExecutions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EvalSuites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    TargetAgentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvalSuites", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EvalCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    SuiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    InputPayload = table.Column<string>(type: "jsonb", nullable: false),
                    ExpectedCriteria = table.Column<string>(type: "jsonb", nullable: false),
                    ScorerType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RegressionThreshold = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                    Tags = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, defaultValue: ""),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvalCases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvalCases_EvalSuites_SuiteId",
                        column: x => x.SuiteId,
                        principalTable: "EvalSuites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EvalRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    SuiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    TriggeredAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "NOW()"),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Pending"),
                    BaselineRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubjectVersion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvalRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvalRuns_EvalRuns_BaselineRunId",
                        column: x => x.BaselineRunId,
                        principalTable: "EvalRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EvalRuns_EvalSuites_SuiteId",
                        column: x => x.SuiteId,
                        principalTable: "EvalSuites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EvalResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    EvalRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvalCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentExecutionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ScorerType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ScorerVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Score = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                    Passed = table.Column<bool>(type: "boolean", nullable: false),
                    ScorerOutput = table.Column<string>(type: "jsonb", nullable: false),
                    ScoredAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvalResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvalResults_EvalCases_EvalCaseId",
                        column: x => x.EvalCaseId,
                        principalTable: "EvalCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EvalResults_EvalRuns_EvalRunId",
                        column: x => x.EvalRunId,
                        principalTable: "EvalRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CostLedger_EvalRunId",
                table: "CostLedger",
                column: "EvalRunId");

            migrationBuilder.CreateIndex(
                name: "IX_CostLedger_Source",
                table: "CostLedger",
                column: "Source");

            migrationBuilder.CreateIndex(
                name: "IX_AgentExecutions_EvalRunId",
                table: "AgentExecutions",
                column: "EvalRunId");

            migrationBuilder.CreateIndex(
                name: "IX_EvalCases_SuiteId",
                table: "EvalCases",
                column: "SuiteId");

            migrationBuilder.CreateIndex(
                name: "IX_EvalResults_AgentExecutionId",
                table: "EvalResults",
                column: "AgentExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_EvalResults_EvalCaseId",
                table: "EvalResults",
                column: "EvalCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_EvalResults_EvalRunId_EvalCaseId",
                table: "EvalResults",
                columns: new[] { "EvalRunId", "EvalCaseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EvalRuns_BaselineRunId",
                table: "EvalRuns",
                column: "BaselineRunId");

            migrationBuilder.CreateIndex(
                name: "IX_EvalRuns_Status",
                table: "EvalRuns",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_EvalRuns_SuiteId",
                table: "EvalRuns",
                column: "SuiteId");

            migrationBuilder.CreateIndex(
                name: "IX_EvalSuites_TargetAgentType",
                table: "EvalSuites",
                column: "TargetAgentType");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EvalResults");

            migrationBuilder.DropTable(
                name: "EvalCases");

            migrationBuilder.DropTable(
                name: "EvalRuns");

            migrationBuilder.DropTable(
                name: "EvalSuites");

            migrationBuilder.DropIndex(
                name: "IX_CostLedger_EvalRunId",
                table: "CostLedger");

            migrationBuilder.DropIndex(
                name: "IX_CostLedger_Source",
                table: "CostLedger");

            migrationBuilder.DropIndex(
                name: "IX_AgentExecutions_EvalRunId",
                table: "AgentExecutions");

            migrationBuilder.DropColumn(
                name: "EvalRunId",
                table: "CostLedger");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "CostLedger");

            migrationBuilder.DropColumn(
                name: "EvalRunId",
                table: "AgentExecutions");
        }
    }
}
