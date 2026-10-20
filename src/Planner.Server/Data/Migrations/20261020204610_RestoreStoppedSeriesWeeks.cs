using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Planner.Server.Data.Migrations
{
    /// <summary>
    /// Пропавшие недели постоянной серии (docs/specs/fixes-2026-10.md, E1.3; решение Анны 2026-10-20). До E1.1 выключение «каждую неделю»
    /// и удаление «с этой недели и дальше» удаляли цели следующих недель, но не откатывали GeneratedThrough — снова включённая серия
    /// эти недели не создавала. Схема не меняется.
    /// <list type="number">
    /// <item>Законченные серии: GeneratedThrough = LastWeek (как теперь делает StopAfter) — снова включённые, они создадут недели заново.</item>
    /// <item>«Тренировка со скакалкой» (выключили и включили «каждую неделю» на неделе 5 окт): цели 12, 19 и 26 окт по нынешнему шаблону
    /// серии — без занятий (дни не выбраны), в конец списка каждой роли своей недели; уже существующие недели не трогаются.
    /// Занятия старого шаблона из корзины не возвращаются.</item>
    /// </list>
    /// </summary>
    public partial class RestoreStoppedSeriesWeeks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE GoalSeries SET GeneratedThrough = LastWeek
                WHERE LastWeek IS NOT NULL AND GeneratedThrough > LastWeek;

                CREATE TEMP TABLE RestoredWeeks AS
                SELECT s.Id AS SeriesId, w.Week AS WeekStart
                FROM GoalSeries s
                JOIN (SELECT '2026-10-12' AS Week UNION ALL SELECT '2026-10-19' UNION ALL SELECT '2026-10-26') w
                WHERE s.Title = 'Тренировка со скакалкой' AND s.IsRecurring = 1 AND s.LastWeek IS NULL AND s.Days IS NULL
                  AND w.Week > s.FirstWeek AND w.Week <= s.GeneratedThrough
                  AND NOT EXISTS (SELECT 1 FROM WeeklyGoals g WHERE g.SeriesId = s.Id AND g.WeekStart = w.Week);

                INSERT INTO WeeklyGoals (AllowSeveralPerDay, Days, DurationMinutes, Notes, ProjectId, Quadrant, SeriesId, StartTime, TimesPerWeek, Title, WeekStart)
                SELECT s.AllowSeveralPerDay, s.Days, s.DurationMinutes, s.Notes, s.ProjectId, s.Quadrant, s.Id, s.StartTime, s.TimesPerWeek, s.Title, r.WeekStart
                FROM RestoredWeeks r JOIN GoalSeries s ON s.Id = r.SeriesId
                ORDER BY r.WeekStart;

                INSERT INTO GoalRoles (GoalId, RoleId, SortOrder)
                SELECT g.Id, sr.RoleId,
                       COALESCE((SELECT MAX(gr.SortOrder) FROM GoalRoles gr JOIN WeeklyGoals o ON o.Id = gr.GoalId
                                 WHERE o.WeekStart = g.WeekStart AND gr.RoleId = sr.RoleId), -1) + 1
                FROM WeeklyGoals g
                JOIN RestoredWeeks r ON r.SeriesId = g.SeriesId AND r.WeekStart = g.WeekStart
                JOIN GoalSeriesRoles sr ON sr.SeriesId = g.SeriesId;

                DROP TABLE RestoredWeeks;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Данные не возвращаются: восстановленные цели — то, что серия создала бы без бага; их можно удалить в окне цели
        }
    }
}
