using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrchestAI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExtendCostRollupUniqueIndexWithTenantId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CostRollups_Date_UserId_AgentType_Model",
                table: "CostRollups");

            migrationBuilder.CreateIndex(
                name: "IX_CostRollups_Date_TenantId_UserId_AgentType_Model",
                table: "CostRollups",
                columns: new[] { "Date", "TenantId", "UserId", "AgentType", "Model" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CostRollups_Date_TenantId_UserId_AgentType_Model",
                table: "CostRollups");

            migrationBuilder.CreateIndex(
                name: "IX_CostRollups_Date_UserId_AgentType_Model",
                table: "CostRollups",
                columns: new[] { "Date", "UserId", "AgentType", "Model" },
                unique: true);
        }
    }
}
