using Microsoft.EntityFrameworkCore;
using Planner.Core.Abstractions;
using Planner.Core.Entities;
using Planner.Core.Enums;
using Planner.Core.Services;
using Planner.Core.Time;
using Planner.Shared;

namespace Planner.Server.Endpoints;

/// <summary>Чтение экрана целиком: общая часть (лента, роли) и конкретная неделя.</summary>
public static class BoardEndpoints
{
    public static void MapBoardEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/board", GetBoardAsync);
        api.MapGet("/week/{weekStart}", GetWeekAsync);
    }

    private static async Task<BoardDto> GetBoardAsync(
        IRoleRepository roles,
        ICategoryRepository categories,
        ITaskRepository tasks,
        TaskViews views,
        CancellationToken ct)
    {
        var allRoles = await roles.GetAllAsync(ct);
        var allCategories = await categories.GetAllAsync(ct);
        var someday = await tasks.GetSomedayAsync(ct);
        var plannedProjects = await tasks.GetPlannedProjectsAsync(ct);
        var trashed = await tasks.GetTrashedAsync(ct);

        return new BoardDto(
            RolePalette.Colors,
            Role.RecommendedGoalsPerWeek,
            allRoles.Select(r => r.ToDto()).ToList(),
            allCategories.Select(c => c.ToDto()).ToList(),
            someday.Select(t => t.ToDto()).ToList(),
            trashed.Count,
            await views.StepsOfAsync(someday.Concat(plannedProjects), ct),
            await views.ToDtosAsync(plannedProjects, ct));
    }

    /// <summary>Неделя по любой её дате (приводится к понедельнику).</summary>
    private static async Task<WeekDto> GetWeekAsync(
        DateOnly weekStart,
        IGoalRepository goals,
        ITaskRepository tasks,
        GoalSeriesService series,
        TaskViews views,
        CancellationToken ct)
    {
        var monday = Week.StartOf(weekStart);

        // Постоянные цели («каждую неделю») появляются на неделе, когда её открывают
        try
        {
            await series.EnsureWeeksAsync(monday, ct);
        }
        catch (DbUpdateException)
        {
            // Ту же неделю одновременно открыли в другом окне — цели уже создал тот запрос
        }

        var weekGoals = await goals.GetForWeekAsync(monday, ct);
        var weekTasks = await tasks.GetPlannedAsync(monday, monday.AddDays(Week.DaysInWeek - 1), ct);

        // И занятия целей без дня: в днях их не видно, но клиенту они нужны целиком
        // (удаление цели или перенос её в ленту сразу показывают это дело в ленте)
        var unscheduled = weekGoals.SelectMany(g => g.ActiveSessions).Where(t => t.State == TaskState.Planned && t.PlannedDate is null);

        // И дела «на неделе, без дня» (N4): в днях их нет, они — в левой панели под своей ролью
        var withoutDay = await tasks.GetWithoutDayOnWeekAsync(monday, ct);

        var shown = weekTasks.Concat(unscheduled).Concat(withoutDay).DistinctBy(t => t.Id).ToList();
        return new WeekDto(
            monday,
            weekGoals.Select(g => g.ToDto()).ToList(),
            await views.ToDtosAsync(shown, ct),
            await views.StepsOfAsync(shown.Concat(weekGoals.Select(g => g.Project).OfType<TaskItem>()).ToList(), ct)); // и шаги проектов целей (G)
    }
}
