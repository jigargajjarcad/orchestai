using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrchestAI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPostHocScoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "SuiteId",
                table: "EvalRuns",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<bool>(
                name: "ForceRescore",
                table: "EvalRuns",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Rubric",
                table: "EvalRuns",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SelectionCriteriaJson",
                table: "EvalRuns",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SkippedAlreadyScoredCount",
                table: "EvalRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "EvalRuns",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "LiveSuite");

            migrationBuilder.AlterColumn<Guid>(
                name: "EvalCaseId",
                table: "EvalResults",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "Rubric",
                table: "EvalResults",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EvalRuns_Source",
                table: "EvalRuns",
                column: "Source");

            migrationBuilder.CreateIndex(
                name: "IX_EvalResults_AgentExecutionId_ScorerType_ScorerVersion",
                table: "EvalResults",
                columns: new[] { "AgentExecutionId", "ScorerType", "ScorerVersion" },
                unique: true,
                filter: "\"EvalCaseId\" IS NULL AND \"AgentExecutionId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EvalRuns_Source",
                table: "EvalRuns");

            migrationBuilder.DropIndex(
                name: "IX_EvalResults_AgentExecutionId_ScorerType_ScorerVersion",
                table: "EvalResults");

            migrationBuilder.DropColumn(
                name: "ForceRescore",
                table: "EvalRuns");

            migrationBuilder.DropColumn(
                name: "Rubric",
                table: "EvalRuns");

            migrationBuilder.DropColumn(
                name: "SelectionCriteriaJson",
                table: "EvalRuns");

            migrationBuilder.DropColumn(
                name: "SkippedAlreadyScoredCount",
                table: "EvalRuns");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "EvalRuns");

            migrationBuilder.DropColumn(
                name: "Rubric",
                table: "EvalResults");

            migrationBuilder.AlterColumn<Guid>(
                name: "SuiteId",
                table: "EvalRuns",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "EvalCaseId",
                table: "EvalResults",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
