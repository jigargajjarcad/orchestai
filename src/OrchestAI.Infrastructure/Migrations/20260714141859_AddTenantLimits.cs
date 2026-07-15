using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrchestAI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantLimits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestsPerMinute = table.Column<int>(type: "integer", nullable: true),
                    MaxConcurrentTasks = table.Column<int>(type: "integer", nullable: true),
                    MaxAgentsPerTask = table.Column<int>(type: "integer", nullable: true),
                    MaxToolCallsPerTask = table.Column<int>(type: "integer", nullable: true),
                    DailyCostBudgetUsd = table.Column<decimal>(type: "numeric(18,6)", nullable: true),
                    MonthlyCostBudgetUsd = table.Column<decimal>(type: "numeric(18,6)", nullable: true),
                    MaxQueueDepth = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantLimits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantLimits_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantLimits_TenantId",
                table: "TenantLimits",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantLimits");
        }
    }
}
