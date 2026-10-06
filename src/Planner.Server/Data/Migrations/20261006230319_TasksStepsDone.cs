using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Planner.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class TasksStepsDone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "IsHabitDeleted",
                table: "GoalSeries",
                newName: "IsHabit");

            migrationBuilder.AddColumn<int>(
                name: "ProjectId",
                table: "WeeklyGoals",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClipSeriesId",
                table: "Tasks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFromSomeday",
                table: "Tasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "StepOrder",
                table: "Tasks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProjectId",
                table: "GoalSeries",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyGoals_ProjectId",
                table: "WeeklyGoals",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_ClipSeriesId",
                table: "Tasks",
                column: "ClipSeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_State_IsFromSomeday_CompletedAt",
                table: "Tasks",
                columns: new[] { "State", "IsFromSomeday", "CompletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_GoalSeries_ProjectId",
                table: "GoalSeries",
                column: "ProjectId");

            migrationBuilder.AddForeignKey(
                name: "FK_GoalSeries_Tasks_ProjectId",
                table: "GoalSeries",
                column: "ProjectId",
                principalTable: "Tasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_GoalSeries_ClipSeriesId",
                table: "Tasks",
                column: "ClipSeriesId",
                principalTable: "GoalSeries",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_WeeklyGoals_Tasks_ProjectId",
                table: "WeeklyGoals",
                column: "ProjectId",
                principalTable: "Tasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // «🫙 копить»: после переименования в IsHabit пока лежит прежний IsHabitDeleted. Привычкой была постоянная,
            // не удалённая серия хотя бы с одним измерением пилы — она и копит; остальные — нет
            migrationBuilder.Sql("""
                UPDATE GoalSeries SET IsHabit = CASE
                    WHEN IsRecurring = 1 AND IsHabit = 0 AND EXISTS (
                        SELECT 1 FROM GoalSeriesRoles sr JOIN Roles r ON r.Id = sr.RoleId
                        WHERE sr.SeriesId = GoalSeries.Id AND r.SawDimension IS NOT NULL)
                    THEN 1 ELSE 0 END;
                """);

            // Дело из ленты: лежит в ленте, хранит категорию (вынесено из ленты в план) или ушло в корзину из ленты.
            // Мысли «неразобранного», уже вынесенные в план, отличить нельзя — они остаются делами недели
            migrationBuilder.Sql("""
                UPDATE Tasks SET IsFromSomeday = 1
                WHERE State = 'Someday' OR CategoryId IS NOT NULL OR (State = 'Trashed' AND PreviousState = 'Someday');
                """);

            // Порядок шагов в списке родителя — прежний SortOrder шага
            migrationBuilder.Sql("UPDATE Tasks SET StepOrder = SortOrder WHERE ParentId IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Обратно: «удалена как привычка» — постоянная серия без «копить»
            migrationBuilder.Sql("UPDATE GoalSeries SET IsHabit = CASE WHEN IsRecurring = 1 AND IsHabit = 0 THEN 1 ELSE 0 END;");

            migrationBuilder.DropForeignKey(
                name: "FK_GoalSeries_Tasks_ProjectId",
                table: "GoalSeries");

            migrationBuilder.DropForeignKey(
                name: "FK_Tasks_GoalSeries_ClipSeriesId",
                table: "Tasks");

            migrationBuilder.DropForeignKey(
                name: "FK_WeeklyGoals_Tasks_ProjectId",
                table: "WeeklyGoals");

            migrationBuilder.DropIndex(
                name: "IX_WeeklyGoals_ProjectId",
                table: "WeeklyGoals");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_ClipSeriesId",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_State_IsFromSomeday_CompletedAt",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_GoalSeries_ProjectId",
                table: "GoalSeries");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "WeeklyGoals");

            migrationBuilder.DropColumn(
                name: "ClipSeriesId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "IsFromSomeday",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "StepOrder",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "GoalSeries");

            migrationBuilder.RenameColumn(
                name: "IsHabit",
                table: "GoalSeries",
                newName: "IsHabitDeleted");
        }
    }
}
