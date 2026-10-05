using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Planner.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class Habits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HabitId",
                table: "WeeklyGoals",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HabitId",
                table: "Tasks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Quantity",
                table: "Tasks",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HabitId",
                table: "GoalSeries",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Habits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Emoji = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Color = table.Column<string>(type: "TEXT", maxLength: 7, nullable: false),
                    WeeklyNorm = table.Column<int>(type: "INTEGER", nullable: false),
                    JarSize = table.Column<int>(type: "INTEGER", nullable: false),
                    StartCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Habits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Vacations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    From = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    To = table.Column<DateOnly>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vacations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VacationHabits",
                columns: table => new
                {
                    HabitId = table.Column<int>(type: "INTEGER", nullable: false),
                    VacationId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VacationHabits", x => new { x.VacationId, x.HabitId });
                    table.ForeignKey(
                        name: "FK_VacationHabits_Habits_HabitId",
                        column: x => x.HabitId,
                        principalTable: "Habits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VacationHabits_Vacations_VacationId",
                        column: x => x.VacationId,
                        principalTable: "Vacations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyGoals_HabitId",
                table: "WeeklyGoals",
                column: "HabitId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_HabitId_State",
                table: "Tasks",
                columns: new[] { "HabitId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_GoalSeries_HabitId",
                table: "GoalSeries",
                column: "HabitId");

            migrationBuilder.CreateIndex(
                name: "IX_VacationHabits_HabitId",
                table: "VacationHabits",
                column: "HabitId");

            migrationBuilder.AddForeignKey(
                name: "FK_GoalSeries_Habits_HabitId",
                table: "GoalSeries",
                column: "HabitId",
                principalTable: "Habits",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_Habits_HabitId",
                table: "Tasks",
                column: "HabitId",
                principalTable: "Habits",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_WeeklyGoals_Habits_HabitId",
                table: "WeeklyGoals",
                column: "HabitId",
                principalTable: "Habits",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GoalSeries_Habits_HabitId",
                table: "GoalSeries");

            migrationBuilder.DropForeignKey(
                name: "FK_Tasks_Habits_HabitId",
                table: "Tasks");

            migrationBuilder.DropForeignKey(
                name: "FK_WeeklyGoals_Habits_HabitId",
                table: "WeeklyGoals");

            migrationBuilder.DropTable(
                name: "VacationHabits");

            migrationBuilder.DropTable(
                name: "Habits");

            migrationBuilder.DropTable(
                name: "Vacations");

            migrationBuilder.DropIndex(
                name: "IX_WeeklyGoals_HabitId",
                table: "WeeklyGoals");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_HabitId_State",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_GoalSeries_HabitId",
                table: "GoalSeries");

            migrationBuilder.DropColumn(
                name: "HabitId",
                table: "WeeklyGoals");

            migrationBuilder.DropColumn(
                name: "HabitId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "HabitId",
                table: "GoalSeries");
        }
    }
}
