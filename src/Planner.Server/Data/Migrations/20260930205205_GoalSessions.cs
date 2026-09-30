using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Planner.Server.Data.Migrations
{
    /// <summary>
    /// Цели с повторами: у цели «раз в неделю» (TimesPerWeek) и несколько занятий — дел с GoalId.
    /// Связь переворачивается: было WeeklyGoals.TaskId (одно дело), стало Tasks.GoalId.
    /// «Выполнено» у цели теперь считается по занятиям, поэтому WeeklyGoals.IsDone уходит:
    /// цель, отмеченная без дела, получает выполненное занятие без дня — прогресс остаётся 1/1.
    /// </summary>
    public partial class GoalSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GoalId",
                table: "Tasks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TimesPerWeek",
                table: "WeeklyGoals",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            // Данные переносим до того, как старые столбцы исчезнут
            migrationBuilder.Sql("""
                UPDATE Tasks
                SET GoalId = (SELECT g.Id FROM WeeklyGoals g WHERE g.TaskId = Tasks.Id)
                WHERE Id IN (SELECT TaskId FROM WeeklyGoals WHERE TaskId IS NOT NULL);

                INSERT INTO Tasks (Title, RoleId, State, SortOrder, CreatedAt, CompletedAt, GoalId)
                SELECT g.Title, g.RoleId, 'Done', 0,
                       strftime('%Y-%m-%d %H:%M:%S', 'now'), strftime('%Y-%m-%d %H:%M:%S', 'now'), g.Id
                FROM WeeklyGoals g
                WHERE g.IsDone = 1 AND g.TaskId IS NULL;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_WeeklyGoals_Tasks_TaskId",
                table: "WeeklyGoals");

            migrationBuilder.DropIndex(
                name: "IX_WeeklyGoals_TaskId",
                table: "WeeklyGoals");

            migrationBuilder.DropColumn(
                name: "TaskId",
                table: "WeeklyGoals");

            migrationBuilder.DropColumn(
                name: "IsDone",
                table: "WeeklyGoals");

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TaskId",
                table: "WeeklyGoals",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDone",
                table: "WeeklyGoals",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Назад — одно дело на цель: первое занятие; «выполнено» — если отмечено нужное число
            migrationBuilder.Sql("""
                UPDATE WeeklyGoals
                SET TaskId = (SELECT MIN(t.Id) FROM Tasks t WHERE t.GoalId = WeeklyGoals.Id AND t.State <> 'Trashed'),
                    IsDone = (SELECT COUNT(*) FROM Tasks t WHERE t.GoalId = WeeklyGoals.Id AND t.State = 'Done')
                             >= TimesPerWeek;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Tasks_WeeklyGoals_GoalId",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_GoalId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "GoalId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "TimesPerWeek",
                table: "WeeklyGoals");

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyGoals_TaskId",
                table: "WeeklyGoals",
                column: "TaskId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_WeeklyGoals_Tasks_TaskId",
                table: "WeeklyGoals",
                column: "TaskId",
                principalTable: "Tasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
