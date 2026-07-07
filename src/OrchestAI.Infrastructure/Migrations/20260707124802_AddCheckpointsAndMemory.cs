using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrchestAI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckpointsAndMemory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentMemories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false),
                    Importance = table.Column<int>(type: "integer", nullable: false, defaultValue: 5),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "NOW()"),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentMemories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentMemories_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaskCheckpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    OrchestrationTaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AgentExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Output = table.Column<string>(type: "text", nullable: false),
                    InputTokens = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CostUsd = table.Column<decimal>(type: "numeric(10,6)", nullable: false, defaultValue: 0m),
                    CheckpointedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskCheckpoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskCheckpoints_OrchestrationTasks_OrchestrationTaskId",
                        column: x => x.OrchestrationTaskId,
                        principalTable: "OrchestrationTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentMemories_UserId_AgentType",
                table: "AgentMemories",
                columns: new[] { "UserId", "AgentType" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentMemories_UserId_AgentType_Key",
                table: "AgentMemories",
                columns: new[] { "UserId", "AgentType", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentMemories_UserId_Importance",
                table: "AgentMemories",
                columns: new[] { "UserId", "Importance" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_TaskCheckpoints_OrchestrationTaskId_AgentType",
                table: "TaskCheckpoints",
                columns: new[] { "OrchestrationTaskId", "AgentType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentMemories");

            migrationBuilder.DropTable(
                name: "TaskCheckpoints");
        }
    }
}
