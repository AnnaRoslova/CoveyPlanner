using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Planner.Server.Data.Migrations
{
    /// <summary>
    /// Давно лежащие мысли (docs/specs/batch-ux.md, BS): UpdatedAt = CreatedAt (возраст — честно, от записи), KeptAt, StaleNotifiedAt;
    /// мыслям, уже старым при установке (срок по умолчанию — 3 мес), — StaleNotifiedAt = сейчас: плашек о них нет (BS4a).
    /// Таблица AppSettings — одна строка со сроком 3 мес.
    /// </summary>
    public partial class StaleThoughts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "KeptAt",
                table: "Tasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StaleNotifiedAt",
                table: "Tasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Tasks",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    StaleMonths = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Id);
                });

            migrationBuilder.Sql("""
                UPDATE Tasks SET UpdatedAt = CreatedAt;

                UPDATE Tasks SET StaleNotifiedAt = strftime('%Y-%m-%d %H:%M:%f', 'now')
                WHERE State = 'Someday' AND ParentId IS NULL AND IsProject = 0
                  AND datetime(CreatedAt, '+3 months') <= datetime('now');

                INSERT INTO AppSettings (Id, StaleMonths) VALUES (1, 3);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSettings");

            migrationBuilder.DropColumn(
                name: "KeptAt",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "StaleNotifiedAt",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Tasks");
        }
    }
}
