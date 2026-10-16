using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Planner.Server.Data.Migrations
{
    /// <summary>
    /// Привычка ≠ проект (docs/specs/fixes-2026-10.md, A3): проекты, которые G3 сам создал у постоянных целей («Чтение», «Силовая тренировка»), —
    /// ошибка, а не выбор. Авто-проект привычки — дело без родителя в ленте или в плане, выбранное проектом постоянной серии с тем же названием,
    /// что у серии (так его называет G3), и без шагов в счёте вне корзины (только занятия целей). Такой проект отвязывается от серии
    /// и всех её целей, его занятия — от него (остаются в днях и целях, скрепки не меняются, как S23), сам он уходит в корзину.
    /// Книга с другим названием и проект с настоящими шагами не трогаются. Схема не меняется.
    /// </summary>
    public partial class HabitsAreNotProjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TEMP TABLE HabitProjects AS
                SELECT DISTINCT p.Id FROM Tasks p
                JOIN GoalSeries s ON s.ProjectId = p.Id AND s.IsRecurring = 1 AND s.Title = p.Title
                WHERE p.ParentId IS NULL AND p.State IN ('Someday', 'Planned')
                  AND NOT EXISTS (
                      SELECT 1 FROM Tasks c
                      WHERE c.ParentId = p.Id AND c.State <> 'Trashed'
                        AND NOT EXISTS (SELECT 1 FROM TaskGoals tg WHERE tg.TaskId = c.Id));

                UPDATE GoalSeries SET ProjectId = NULL WHERE ProjectId IN (SELECT Id FROM HabitProjects);
                UPDATE WeeklyGoals SET ProjectId = NULL WHERE ProjectId IN (SELECT Id FROM HabitProjects);
                UPDATE Tasks SET ParentId = NULL, StepOrder = NULL
                WHERE ParentId IN (SELECT Id FROM HabitProjects) AND State <> 'Trashed';
                UPDATE Tasks SET PreviousState = State, State = 'Trashed', TrashedAt = strftime('%Y-%m-%d %H:%M:%f', 'now')
                WHERE Id IN (SELECT Id FROM HabitProjects);

                DROP TABLE HabitProjects;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Данные не возвращаются: проекты — в корзине (их можно вернуть оттуда), связь занятий с ними была ошибкой
        }
    }
}
