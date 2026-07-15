using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrchestAI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRejectionEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RejectionEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RequestId = table.Column<string>(type: "text", nullable: true),
                    TraceId = table.Column<string>(type: "text", nullable: true),
                    ApiKeyId = table.Column<Guid>(type: "uuid", nullable: true),
                    DetailsJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RejectionEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RejectionEvents_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RejectionEvents_TenantId_OccurredAt",
                table: "RejectionEvents",
                columns: new[] { "TenantId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RejectionEvents");
        }
    }
}
