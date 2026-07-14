using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrchestAI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_AgentExecutions_Tenants_TenantId",
                table: "AgentExecutions",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AgentMemories_Tenants_TenantId",
                table: "AgentMemories",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AgentMessages_Tenants_TenantId",
                table: "AgentMessages",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AgentRetryAttempts_Tenants_TenantId",
                table: "AgentRetryAttempts",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CostLedger_Tenants_TenantId",
                table: "CostLedger",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CostRollups_Tenants_TenantId",
                table: "CostRollups",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EvalCases_Tenants_TenantId",
                table: "EvalCases",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EvalResults_Tenants_TenantId",
                table: "EvalResults",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EvalRuns_Tenants_TenantId",
                table: "EvalRuns",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EvalSuites_Tenants_TenantId",
                table: "EvalSuites",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_McpToolCalls_Tenants_TenantId",
                table: "McpToolCalls",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrchestrationTasks_Tenants_TenantId",
                table: "OrchestrationTasks",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TaskCheckpoints_Tenants_TenantId",
                table: "TaskCheckpoints",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AgentExecutions_Tenants_TenantId",
                table: "AgentExecutions");

            migrationBuilder.DropForeignKey(
                name: "FK_AgentMemories_Tenants_TenantId",
                table: "AgentMemories");

            migrationBuilder.DropForeignKey(
                name: "FK_AgentMessages_Tenants_TenantId",
                table: "AgentMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_AgentRetryAttempts_Tenants_TenantId",
                table: "AgentRetryAttempts");

            migrationBuilder.DropForeignKey(
                name: "FK_CostLedger_Tenants_TenantId",
                table: "CostLedger");

            migrationBuilder.DropForeignKey(
                name: "FK_CostRollups_Tenants_TenantId",
                table: "CostRollups");

            migrationBuilder.DropForeignKey(
                name: "FK_EvalCases_Tenants_TenantId",
                table: "EvalCases");

            migrationBuilder.DropForeignKey(
                name: "FK_EvalResults_Tenants_TenantId",
                table: "EvalResults");

            migrationBuilder.DropForeignKey(
                name: "FK_EvalRuns_Tenants_TenantId",
                table: "EvalRuns");

            migrationBuilder.DropForeignKey(
                name: "FK_EvalSuites_Tenants_TenantId",
                table: "EvalSuites");

            migrationBuilder.DropForeignKey(
                name: "FK_McpToolCalls_Tenants_TenantId",
                table: "McpToolCalls");

            migrationBuilder.DropForeignKey(
                name: "FK_OrchestrationTasks_Tenants_TenantId",
                table: "OrchestrationTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_TaskCheckpoints_Tenants_TenantId",
                table: "TaskCheckpoints");
        }
    }
}
