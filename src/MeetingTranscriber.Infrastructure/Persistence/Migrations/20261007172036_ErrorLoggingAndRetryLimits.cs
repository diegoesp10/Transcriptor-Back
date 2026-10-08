using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingTranscriber.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ErrorLoggingAndRetryLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RetryCount",
                schema: "transcriber",
                table: "Meetings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ErrorLogs",
                schema: "transcriber",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MeetingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Endpoint = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    HttpStatus = table.Column<int>(type: "int", nullable: true),
                    ProviderStatus = table.Column<int>(type: "int", nullable: true),
                    ProviderCode = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    ProviderRequestId = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    ChunkIndex = table.Column<int>(type: "int", nullable: true),
                    ExceptionType = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    StackTrace = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ErrorLogs", x => x.Id);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Meetings_RetryCount",
                schema: "transcriber",
                table: "Meetings",
                sql: "[RetryCount] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_ErrorLogs_CorrelationId",
                schema: "transcriber",
                table: "ErrorLogs",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_ErrorLogs_CreatedAt",
                schema: "transcriber",
                table: "ErrorLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ErrorLogs_MeetingId_CreatedAt",
                schema: "transcriber",
                table: "ErrorLogs",
                columns: new[] { "MeetingId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ErrorLogs",
                schema: "transcriber");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Meetings_RetryCount",
                schema: "transcriber",
                table: "Meetings");

            migrationBuilder.DropColumn(
                name: "RetryCount",
                schema: "transcriber",
                table: "Meetings");
        }
    }
}
