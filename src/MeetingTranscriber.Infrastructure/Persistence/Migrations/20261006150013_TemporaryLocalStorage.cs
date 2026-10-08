using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingTranscriber.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TemporaryLocalStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TemporaryExpiresAt",
                schema: "transcriber",
                table: "Meetings",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Meetings_TemporaryExpiresAt",
                schema: "transcriber",
                table: "Meetings",
                column: "TemporaryExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Meetings_TemporaryExpiresAt",
                schema: "transcriber",
                table: "Meetings");

            migrationBuilder.DropColumn(
                name: "TemporaryExpiresAt",
                schema: "transcriber",
                table: "Meetings");
        }
    }
}
