using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Planner.Server.Data.Migrations
{
    /// <summary>«Моя миссия» (docs/specs/batch-ux.md, BM): AppSettings.Mission, AppSettings.ShowMissionInPanel (выкл.), Roles.Vision — пустые.</summary>
    public partial class Mission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Vision",
                table: "Roles",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Mission",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 5000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShowMissionInPanel",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Vision",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "Mission",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "ShowMissionInPanel",
                table: "AppSettings");
        }
    }
}
