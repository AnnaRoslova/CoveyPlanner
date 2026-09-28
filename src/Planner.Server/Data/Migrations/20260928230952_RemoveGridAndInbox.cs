using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Planner.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveGridAndInbox : Migration
    {
        // Старая палитра ролей → палитра «Лотос», цвет с тем же номером
        private static readonly string[] OldPalette =
            ["#6b8fb5", "#5f9e8f", "#7fa66a", "#c2a54f", "#d08a4e", "#c46c6c", "#b56f9b", "#8a76b8", "#8c7a6b", "#7d8791"];

        private static readonly string[] NewPalette =
            ["#f29ea6", "#f4c3cf", "#6fbfae", "#94b4b5", "#a8dcd0", "#f7d3db", "#8ac2c4", "#e9b0b8", "#f5b7a5", "#b9d3b0"];

        private static void RecolorRoles(MigrationBuilder migrationBuilder, string[] from, string[] to)
        {
            for (var i = 0; i < from.Length; i++)
            {
                migrationBuilder.Sql($"UPDATE Roles SET Color = '{to[i]}' WHERE Color = '{from[i]}';");
            }
        }

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            RecolorRoles(migrationBuilder, OldPalette, NewPalette);

            // Бывшая системная категория «Входящие» остаётся обычной категорией (IsSystem удаляется)

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropColumn(
                name: "DurationMinutes",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "IsSystem",
                table: "Categories");

            migrationBuilder.AddColumn<string>(
                name: "Emoji",
                table: "Categories",
                type: "TEXT",
                maxLength: 16,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Emoji",
                table: "Categories");

            migrationBuilder.AddColumn<int>(
                name: "DurationMinutes",
                table: "Tasks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSystem",
                table: "Categories",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    DayEndHour = table.Column<int>(type: "INTEGER", nullable: false),
                    DayStartHour = table.Column<int>(type: "INTEGER", nullable: false),
                    SlotMinutes = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.Sql("INSERT INTO Settings (Id, DayStartHour, DayEndHour, SlotMinutes) VALUES (1, 7, 23, 30);");
            RecolorRoles(migrationBuilder, NewPalette, OldPalette);
        }
    }
}
