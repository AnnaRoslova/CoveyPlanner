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
            var goal = await service.CreateAsync(request.RoleId, request.WeekStart, request.Title, ct);
            return Results.Created($"/api/goals/{goal.Id}", goal.ToDto());
        });

        goals.MapPut("/{id:int}", async (int id, UpdateGoalRequest request, GoalService service, CancellationToken ct) =>
            (await service.UpdateAsync(id, request.Title, request.IsDone, ct)).ToResultDto());

        // ?categoryId= — куда вернуть дело без дня, если у него нет своей категории
        goals.MapDelete("/{id:int}", async (int id, int? categoryId, GoalService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, categoryId, ct);
            return Results.NoContent();
        });

        // Переставить внутри роли или перенести к другой роли
        goals.MapPost("/{id:int}/move", async (int id, MoveGoalRequest request, GoalService service, CancellationToken ct) =>
            (await service.MoveAsync(id, request.RoleId, request.Index, ct)).ToDto());

        // Цель → день / час
        goals.MapPost("/{id:int}/schedule", async (int id, TaskTargetDto target, GoalService service, CancellationToken ct) =>
            (await service.ScheduleAsync(id, target.ToCore(), ct)).ToResultDto());

        // Дело из ленты → цель роли
        goals.MapPost("/from-task", async (GoalFromTaskRequest request, GoalService service, CancellationToken ct) =>
            (await service.CreateFromTaskAsync(request.TaskId, request.RoleId, request.WeekStart, request.Index, ct))
            .ToResultDto());

        // Цель → обратно в ленту (цель удаляется, дело встаёт в категорию)
        goals.MapPost("/{id:int}/to-someday", async (int id, GoalToSomedayRequest request, GoalService service, CancellationToken ct) =>
            (await service.MoveToSomedayAsync(id, request.CategoryId, request.Index, ct)).ToDto());
    }
}
