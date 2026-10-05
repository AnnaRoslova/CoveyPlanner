using Planner.Core.Abstractions;
using Planner.Core.Services;
using Planner.Shared;

namespace Planner.Server.Endpoints;

/// <summary>Дела и корзина.</summary>
public static class TaskEndpoints
{
    public static void MapTaskEndpoints(this RouteGroupBuilder api)
    {
        var tasks = api.MapGroup("/tasks");

        tasks.MapPost("/", async (CreateTaskRequest request, TaskService service, CancellationToken ct) =>
        {
            var task = await service.CreateAsync(request.Title, request.Target.ToCore(), ct);
            return Results.Created($"/api/tasks/{task.Id}", task.ToDto());
        });

        tasks.MapPut("/{id:int}", async (int id, UpdateTaskRequest request, TaskService service, CancellationToken ct) =>
            (await service.UpdateAsync(
                id, request.Title, request.Notes, request.RoleIds ?? [], request.Quadrant.ToQuadrant(), request.StartTime,
                request.DurationMinutes, request.HabitId, ct))
            .ToDto());

        tasks.MapPost("/{id:int}/move", async (int id, TaskTargetDto target, TaskService service, CancellationToken ct) =>
            (await service.MoveAsync(id, target.ToCore(), ct)).ToDto());

        // Занятием какой цели сделать дело (у дела — одна цель); заменённые занятия — в корзину
        tasks.MapPut("/{id:int}/goal", async (int id, SetTaskGoalRequest request, TaskGoalService service, CancellationToken ct) =>
        {
            var update = await service.SetGoalAsync(id, request.GoalId, ct);
            return new TaskGoalResultDto(
                update.Task.ToDto(), update.Goals.Select(g => g.ToDto()).ToList(), update.Replaced.Select(t => t.Id).ToList());
        });

        tasks.MapPut("/{id:int}/done", async (int id, SetDoneRequest request, TaskService service, CancellationToken ct) =>
            (await service.SetDoneAsync(id, request.IsDone, ct)).ToDto());

        // Удаление = в корзину
        tasks.MapDelete("/{id:int}", async (int id, TaskService service, CancellationToken ct) =>
        {
            await service.TrashAsync(id, ct);
            return Results.NoContent();
        });

        var trash = api.MapGroup("/trash");

        trash.MapGet("/", async (ITaskRepository repository, CancellationToken ct) =>
            (await repository.GetTrashedAsync(ct)).Select(t => t.ToDto()).ToList());

        // Дело ленты без своей категории возвращается в «неразобранное»
        trash.MapPost("/{id:int}/restore", async (int id, TaskService service, CancellationToken ct) =>
            (await service.RestoreAsync(id, ct)).ToDto());

        trash.MapDelete("/{id:int}", async (int id, TaskService service, CancellationToken ct) =>
        {
            await service.DeleteForeverAsync(id, ct);
            return Results.NoContent();
        });

        trash.MapDelete("/", async (TaskService service, CancellationToken ct) =>
        {
            await service.EmptyTrashAsync(ct);
            return Results.NoContent();
        });
    }
}
