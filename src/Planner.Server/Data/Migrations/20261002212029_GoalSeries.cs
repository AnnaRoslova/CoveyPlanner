using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Planner.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class GoalSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SeriesId",
                table: "WeeklyGoals",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GoalSeries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoleId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    TimesPerWeek = table.Column<int>(type: "INTEGER", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "TEXT", nullable: true),
                    Quadrant = table.Column<int>(type: "INTEGER", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 10000, nullable: true),
                    DurationMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    IsRecurring = table.Column<bool>(type: "INTEGER", nullable: false),
                    FirstWeek = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    LastWeek = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    GeneratedThrough = table.Column<DateOnly>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoalSeries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GoalSeries_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyGoals_SeriesId_WeekStart",
                table: "WeeklyGoals",
                columns: new[] { "SeriesId", "WeekStart" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GoalSeries_IsRecurring",
                table: "GoalSeries",
                column: "IsRecurring");

            migrationBuilder.CreateIndex(
                name: "IX_GoalSeries_RoleId",
                table: "GoalSeries",
                column: "RoleId");

            migrationBuilder.AddForeignKey(
                name: "FK_WeeklyGoals_GoalSeries_SeriesId",
                table: "WeeklyGoals",
                column: "SeriesId",
                principalTable: "GoalSeries",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Цели «Затачивания пилы» → серии: одна серия на (роль, название), шаблон — из самой поздней недели.
            // Постоянная («каждую неделю») — если цель есть на текущей или прошлой неделе (или позже);
            // более старые — законченные серии, чтобы давно забытые цели не появились снова.
            // Понедельник текущей недели: ближайшее воскресенье (или сегодня) минус 6 дней.
            migrationBuilder.Sql("""
                INSERT INTO GoalSeries (RoleId, Title, TimesPerWeek, StartTime, Quadrant, Notes, DurationMinutes,
                                        IsRecurring, FirstWeek, LastWeek, GeneratedThrough)
                SELECT g.RoleId, g.Title, g.TimesPerWeek, g.StartTime, g.Quadrant, g.Notes, g.DurationMinutes,
                       g.WeekStart >= date('now', 'localtime', 'weekday 0', '-13 days'),
                       (SELECT MIN(f.WeekStart) FROM WeeklyGoals f WHERE f.RoleId = g.RoleId AND f.Title = g.Title),
                       CASE WHEN g.WeekStart >= date('now', 'localtime', 'weekday 0', '-13 days') THEN NULL ELSE g.WeekStart END,
                       g.WeekStart
                FROM WeeklyGoals g
                JOIN Roles r ON r.Id = g.RoleId
                WHERE r.SawDimension IS NOT NULL
                  AND g.Id = (SELECT l.Id FROM WeeklyGoals l WHERE l.RoleId = g.RoleId AND l.Title = g.Title
                              ORDER BY l.WeekStart DESC, l.Id DESC LIMIT 1);
                """);

            // Цели — в свои серии; если на одной неделе две одинаковые цели, в серию входит первая
            migrationBuilder.Sql("""
                UPDATE WeeklyGoals
                SET SeriesId = (SELECT s.Id FROM GoalSeries s WHERE s.RoleId = WeeklyGoals.RoleId AND s.Title = WeeklyGoals.Title)
                WHERE RoleId IN (SELECT Id FROM Roles WHERE SawDimension IS NOT NULL)
                  AND Id = (SELECT MIN(d.Id) FROM WeeklyGoals d
                            WHERE d.RoleId = WeeklyGoals.RoleId AND d.Title = WeeklyGoals.Title AND d.WeekStart = WeeklyGoals.WeekStart);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WeeklyGoals_GoalSeries_SeriesId",
                table: "WeeklyGoals");

            migrationBuilder.DropTable(
                name: "GoalSeries");

            migrationBuilder.DropIndex(
                name: "IX_WeeklyGoals_SeriesId_WeekStart",
                table: "WeeklyGoals");

            migrationBuilder.DropColumn(
                name: "SeriesId",
                table: "WeeklyGoals");
        }
    }
}
