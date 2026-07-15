using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrchestAI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskAdmissionReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TaskAdmissionReservations",
                columns: table => new
                {
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReservedCostUsd = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskAdmissionReservations", x => x.TaskId);
                    table.ForeignKey(
                        name: "FK_TaskAdmissionReservations_OrchestrationTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "OrchestrationTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaskAdmissionReservations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskAdmissionReservations_TenantId_CreatedAt",
                table: "TaskAdmissionReservations",
                columns: new[] { "TenantId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaskAdmissionReservations");
        }
    }
}
