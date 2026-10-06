using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Planner.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class HabitsAreSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Копилки и коллекции удаляются (привычка теперь — постоянная серия пилы). Каникулы, выбранные для копилок,
            // удаляются вместе с ними (иначе пустой список стал бы «для всех»); каникулы «для всех» остаются — уже для привычек
            migrationBuilder.Sql("DELETE FROM Vacations WHERE Id IN (SELECT VacationId FROM VacationHabits);");
            migrationBuilder.Sql("DELETE FROM VacationHabits;");

            migrationBuilder.DropForeignKey(
                name: "FK_GoalSeries_Habits_HabitId",
                table: "GoalSeries");

            migrationBuilder.DropForeignKey(
                name: "FK_Tasks_Habits_HabitId",
                table: "Tasks");

            migrationBuilder.DropForeignKey(
                name: "FK_VacationHabits_Habits_HabitId",
                table: "VacationHabits");

            migrationBuilder.DropForeignKey(
                name: "FK_WeeklyGoals_Habits_HabitId",
                table: "WeeklyGoals");

            migrationBuilder.DropTable(
                name: "HabitCategories");

            migrationBuilder.DropTable(
                name: "Habits");

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

            migrationBuilder.AddColumn<bool>(
                name: "IsHabitDeleted",
                table: "GoalSeries",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "JarSize",
                table: "GoalSeries",
                type: "INTEGER",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<int>(
                name: "StartCount",
                table: "GoalSeries",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddForeignKey(
                name: "FK_VacationHabits_GoalSeries_HabitId",
                table: "VacationHabits",
                column: "HabitId",
                principalTable: "GoalSeries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VacationHabits_GoalSeries_HabitId",
                table: "VacationHabits");

            migrationBuilder.DropColumn(
                name: "IsHabitDeleted",
                table: "GoalSeries");

            migrationBuilder.DropColumn(
                name: "JarSize",
                table: "GoalSeries");

            migrationBuilder.DropColumn(
                name: "StartCount",
                table: "GoalSeries");

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
                    Color = table.Column<string>(type: "TEXT", maxLength: 7, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Emoji = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                    JarSize = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    StartCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    WeeklyNorm = table.Column<int>(type: "INTEGER", nullable: false),
                    YearGoal = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Habits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HabitCategories",
                columns: table => new
                {
                    HabitId = table.Column<int>(type: "INTEGER", nullable: false),
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HabitCategories", x => new { x.HabitId, x.CategoryId });
                    table.ForeignKey(
                        name: "FK_HabitCategories_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_HabitCategories_Habits_HabitId",
                        column: x => x.HabitId,
                        principalTable: "Habits",
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
                name: "IX_HabitCategories_CategoryId",
                table: "HabitCategories",
                column: "CategoryId",
                unique: true);

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
                name: "FK_VacationHabits_Habits_HabitId",
                table: "VacationHabits",
                column: "HabitId",
                principalTable: "Habits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_WeeklyGoals_Habits_HabitId",
                table: "WeeklyGoals",
                column: "HabitId",
                principalTable: "Habits",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
