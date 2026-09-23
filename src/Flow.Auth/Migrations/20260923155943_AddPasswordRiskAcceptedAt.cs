using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flow.Auth.Migrations
{
    /// <inheritdoc />
    public partial class AddPasswordRiskAcceptedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PasswordRiskAcceptedAt",
                schema: "auth",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PasswordRiskAcceptedAt",
                schema: "auth",
                table: "AspNetUsers");
        }
    }
}
