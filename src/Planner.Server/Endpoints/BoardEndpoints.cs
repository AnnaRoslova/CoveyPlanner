using Planner.Core.Abstractions;
using Planner.Core.Entities;
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
        CancellationToken ct)
    {
        var allRoles = await roles.GetAllAsync(ct);
        var allCategories = await categories.GetAllAsync(ct);
        var someday = await tasks.GetSomedayAsync(ct);
        var trashed = await tasks.GetTrashedAsync(ct);
        var lastUsedCategoryId = await tasks.GetLastUsedCategoryIdAsync(ct);

        return new BoardDto(
            RolePalette.Colors,
            Role.RecommendedGoalsPerWeek,
            allRoles.Select(r => r.ToDto()).ToList(),
            allCategories.Select(c => c.ToDto()).ToList(),
            someday.Select(t => t.ToDto()).ToList(),
            trashed.Count,
            lastUsedCategoryId);
    }

    /// <summary>Неделя по любой её дате (приводится к понедельнику).</summary>
    private static async Task<WeekDto> GetWeekAsync(
        DateOnly weekStart,
        IGoalRepository goals,
        ITaskRepository tasks,
        CancellationToken ct)
    {
        var monday = Week.StartOf(weekStart);
        var weekGoals = await goals.GetForWeekAsync(monday, ct);
        var weekTasks = await tasks.GetPlannedAsync(monday, monday.AddDays(Week.DaysInWeek - 1), ct);

        return new WeekDto(
            monday,
            weekGoals.Select(g => g.ToDto()).ToList(),
            weekTasks.Select(t => t.ToDto()).ToList());
    }
}
