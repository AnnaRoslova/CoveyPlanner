using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Planner.Server.Data.Migrations
{
    /// <summary>
    /// Цель — шаблон занятий: у цели время, квадрант, заметки, длительность; у дела — длительность.
    /// </summary>
    public partial class GoalTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DurationMinutes",
                table: "WeeklyGoals",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "WeeklyGoals",
                type: "TEXT",
                maxLength: 10000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Quadrant",
                table: "WeeklyGoals",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "StartTime",
                table: "WeeklyGoals",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DurationMinutes",
                table: "Tasks",
                type: "INTEGER",
                nullable: true);

            // Квадрант у занятий теперь всегда от цели: цель берёт квадрант первого занятия, где он указан,
            // и он же становится у всех её занятий
            migrationBuilder.Sql("""
                UPDATE WeeklyGoals
                SET Quadrant = (SELECT t.Quadrant FROM Tasks t
                                WHERE t.GoalId = WeeklyGoals.Id AND t.Quadrant IS NOT NULL
                                ORDER BY t.Id LIMIT 1);

                UPDATE Tasks
                SET Quadrant = (SELECT g.Quadrant FROM WeeklyGoals g WHERE g.Id = Tasks.GoalId)
                WHERE GoalId IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DurationMinutes",
                table: "WeeklyGoals");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "WeeklyGoals");

            migrationBuilder.DropColumn(
                name: "Quadrant",
                table: "WeeklyGoals");

            migrationBuilder.DropColumn(
                name: "StartTime",
                table: "WeeklyGoals");

            migrationBuilder.DropColumn(
                name: "DurationMinutes",
                table: "Tasks");
        }
    }
}
