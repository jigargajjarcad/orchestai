using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrchestAI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHumanInTheLoopColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApprovalNote",
                table: "OrchestrationTasks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovalRequestedAt",
                table: "OrchestrationTasks",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovalStatus",
                table: "OrchestrationTasks",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedAt",
                table: "OrchestrationTasks",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequireApproval",
                table: "OrchestrationTasks",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovalNote",
                table: "OrchestrationTasks");

            migrationBuilder.DropColumn(
                name: "ApprovalRequestedAt",
                table: "OrchestrationTasks");

            migrationBuilder.DropColumn(
                name: "ApprovalStatus",
                table: "OrchestrationTasks");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "OrchestrationTasks");

            migrationBuilder.DropColumn(
                name: "RequireApproval",
                table: "OrchestrationTasks");
        }
    }
}
