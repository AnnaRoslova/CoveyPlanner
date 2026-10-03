using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Planner.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class TaskGoals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Сначала новая таблица и перенос связей «дело — цель», потом удаление старой колонки — занятия не теряются
            migrationBuilder.CreateTable(
                name: "TaskGoals",
                columns: table => new
                {
                    TaskId = table.Column<int>(type: "INTEGER", nullable: false),
                    GoalId = table.Column<int>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskGoals", x => new { x.TaskId, x.GoalId });
                    table.ForeignKey(
                        name: "FK_TaskGoals_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaskGoals_WeeklyGoals_GoalId",
                        column: x => x.GoalId,
                        principalTable: "WeeklyGoals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskGoals_GoalId",
                table: "TaskGoals",
                column: "GoalId");

            migrationBuilder.Sql("INSERT INTO TaskGoals (TaskId, GoalId, Position) SELECT Id, GoalId, 0 FROM Tasks WHERE GoalId IS NOT NULL;");

            migrationBuilder.DropForeignKey(
                name: "FK_Tasks_WeeklyGoals_GoalId",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_GoalId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "GoalId",
                table: "Tasks");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaskGoals");

            migrationBuilder.AddColumn<int>(
                name: "GoalId",
                table: "Tasks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_GoalId",
                table: "Tasks",
                column: "GoalId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_WeeklyGoals_GoalId",
                table: "Tasks",
                column: "GoalId",
                principalTable: "WeeklyGoals",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
