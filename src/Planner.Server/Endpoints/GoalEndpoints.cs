using Planner.Core.Enums;
using Planner.Core.Services;
using Planner.Shared;

namespace Planner.Server.Endpoints;

/// <summary>Цели ролей на неделю. Список приходит в /api/week/{weekStart}.</summary>
public static class GoalEndpoints
{
    public static void MapGoalEndpoints(this RouteGroupBuilder api)
    {
        var goals = api.MapGroup("/goals");

        goals.MapPost("/", async (CreateGoalRequest request, GoalService service, CancellationToken ct) =>
        {
            var goal = await service.CreateAsync(request.RoleId, request.WeekStart, request.Title, request.IsRecurring, ct);
            return Results.Created($"/api/goals/{goal.Id}", goal.ToDto());
        });

        goals.MapPut("/{id:int}", async (int id, UpdateGoalRequest request, GoalService service, CancellationToken ct) =>
            (await service.UpdateAsync(id, new GoalChanges(
                request.Title, request.RoleIds, request.TimesPerWeek, request.StartTime,
                request.Quadrant.ToQuadrant(), request.Notes, request.DurationMinutes, request.IsRecurring,
                request.AllowSeveralPerDay, (Weekdays?)request.Days), ct))
            .ToUpdateResultDto());

        // Клик по отметке цели: отметить ближайшее занятие или снять отметку с последнего
        goals.MapPost("/{id:int}/toggle", async (int id, ToggleGoalRequest request, GoalService service, CancellationToken ct) =>
            (await service.ToggleDoneAsync(id, request.Today, ct)).ToResultDto());

        // Выполненные занятия остаются в неделе, невыполненные — в корзину, занятие без дня — в ленту
        // (в свою категорию или в «неразобранное»). Постоянная цель: ?scope=series — с этой недели и дальше,
        // иначе — только на этой неделе
        goals.MapDelete("/{id:int}", async (int id, string? scope, GoalService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, fromThisWeekOn: scope == "series", ct);
            return Results.NoContent();
        });

        // Переставить внутри роли, перенести к другой роли или (с Shift) добавить роль
        goals.MapPost("/{id:int}/move", async (int id, MoveGoalRequest request, GoalService service, CancellationToken ct) =>
            (await service.MoveAsync(id, request.RoleId, request.Index, request.FromRoleId, request.AddRole, ct)).ToDto());

        // Цель → день: новое занятие
        goals.MapPost("/{id:int}/schedule", async (int id, TaskTargetDto target, GoalService service, CancellationToken ct) =>
            (await service.ScheduleAsync(id, target.ToCore(), ct)).ToResultDto());

        // Дело из ленты → цель роли
        goals.MapPost("/from-task", async (GoalFromTaskRequest request, GoalService service, CancellationToken ct) =>
            (await service.CreateFromTaskAsync(request.TaskId, request.RoleId, request.WeekStart, request.Index, ct))
            .ToResultDto());

        // Цель → обратно в ленту (цель удаляется, в категорию встаёт её мысль)
        goals.MapPost("/{id:int}/to-someday", async (int id, GoalToSomedayRequest request, GoalService service, CancellationToken ct) =>
            (await service.MoveToSomedayAsync(id, request.CategoryId, request.Index, ct)).ToDto());
    }
}
