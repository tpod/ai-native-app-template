using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppTemplate.Api.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutboxDeliveries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HistoryEntryId = table.Column<long>(type: "INTEGER", nullable: false),
                    Subscriber = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AggregateType = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AggregateId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutboxDeliveries_History_HistoryEntryId",
                        column: x => x.HistoryEntryId,
                        principalTable: "History",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxDeliveries_HistoryEntryId",
                table: "OutboxDeliveries",
                column: "HistoryEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxDeliveries_Status_NextAttemptAt",
                table: "OutboxDeliveries",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxDeliveries_Subscriber_AggregateType_AggregateId_Id",
                table: "OutboxDeliveries",
                columns: new[] { "Subscriber", "AggregateType", "AggregateId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutboxDeliveries");
        }
    }
}
