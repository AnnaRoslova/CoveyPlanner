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

        tasks.MapPost("/", async (CreateTaskRequest request, TaskService service, TaskViews views, CancellationToken ct) =>
        {
            var task = await service.CreateAsync(request.Title, request.Target.ToCore(), ct, request.IsProject);
            return Results.Created($"/api/tasks/{task.Id}", await views.ToDtoAsync(task, ct));
        });

        // Одно дело (например, проект шага, стоящий на другой неделе, — для его окна)
        tasks.MapGet("/{id:int}", async (int id, ITaskRepository repository, TaskViews views, CancellationToken ct) =>
            await repository.FindAsync(id, ct) is { } task ? Results.Ok(await views.ToDtoAsync(task, ct)) : Results.NotFound());

        tasks.MapPut("/{id:int}", async (int id, UpdateTaskRequest request, TaskService service, TaskViews views, CancellationToken ct) =>
            await views.ToDtoAsync(await service.UpdateAsync(
                id, request.Title, request.Notes, request.RoleIds ?? [], request.Quadrant.ToQuadrant(), request.StartTime,
                request.DurationMinutes, ct), ct));

        tasks.MapPost("/{id:int}/move", async (int id, TaskTargetDto target, TaskService service, TaskViews views, CancellationToken ct) =>
            await views.ToDtoAsync(await service.MoveAsync(id, target.ToCore(), ct), ct));

        // Занятием какой цели сделать дело (у дела — одна цель); заменённые занятия — в корзину
        tasks.MapPut("/{id:int}/goal", async (int id, SetTaskGoalRequest request, TaskGoalService service, CancellationToken ct) =>
        {
            var update = await service.SetGoalAsync(id, request.GoalId, ct);
            return new TaskGoalResultDto(
                update.Task.ToDto(), update.Goals.Select(g => g.ToDto()).ToList(), update.Replaced.Select(t => t.Id).ToList());
        });

        // Отметить; отметили последний шаг — в ответе и родитель, отмеченный сам
        tasks.MapPut("/{id:int}/done", async (int id, SetDoneRequest request, TaskService service, TaskViews views, CancellationToken ct) =>
        {
            var done = await service.SetDoneAsync(id, request.IsDone, ct, request.ViaSessionId);
            return new TaskDoneResultDto(
                await views.ToDtoAsync(done.Task, ct), done.Parent is { } parent ? await views.ToDtoAsync(parent, ct) : null,
                done.Session is { } session ? await views.ToDtoAsync(session, ct) : null);
        });

        // «Проект» в карточке: сделать дело шагом другого дела или убрать из проекта
        // Пропуск (docs/specs/skips.md, K, S): «не надо» / «не сделала» с причиной; отменить; история
        tasks.MapPut("/{id:int}/skip", async (int id, SkipTaskRequest request, SkipService service, TaskViews views, CancellationToken ct) =>
        {
            if (!Enum.TryParse<Planner.Core.Enums.SkipKind>(request.Kind, out var kind) || !Enum.IsDefined(kind))
            {
                throw new Planner.Core.Errors.DomainException("Вид пропуска — NotNeeded или Missed.");
            }

            var skipped = await service.SkipAsync(id, kind, request.Reason, ct);
            return new TaskSkipResultDto(
                await views.ToDtoAsync(skipped.Task, ct),
                skipped.Parent is { } parent ? await views.ToDtoAsync(parent, ct) : null,
                skipped.ReturnedToProject);
        });

        // returnTo — «Вернуть» шаг, ушедший при «не сделала» в список проекта, в его день (S2)
        tasks.MapDelete("/{id:int}/skip", async (int id, DateOnly? returnTo, SkipService service, TaskViews views, CancellationToken ct) =>
            await views.ToDtoAsync(await service.UnskipAsync(id, returnTo, ct), ct));

        tasks.MapGet("/{id:int}/skips", async (int id, SkipService service, CancellationToken ct) =>
            (await service.HistoryAsync(id, ct)).Select(s => s.ToDto()).ToList());

        tasks.MapPut("/{id:int}/parent", async (int id, SetParentRequest request, TaskService service, TaskViews views, CancellationToken ct) =>
            await views.ToDtoAsync(await service.SetParentAsync(id, request.ParentId, ct), ct));

        // «🛠 Сделать проектом» / «🛠 больше не проект» (N6): без шагов — снова мысль ленты
        tasks.MapPut("/{id:int}/project", async (int id, SetProjectRequest request, TaskService service, TaskViews views, CancellationToken ct) =>
            await views.ToDtoAsync(await service.SetProjectAsync(id, request.IsProject, ct), ct));

        // Удаление = в корзину
        tasks.MapDelete("/{id:int}", async (int id, TaskService service, CancellationToken ct) =>
        {
            await service.TrashAsync(id, ct);
            return Results.NoContent();
        });

        var trash = api.MapGroup("/trash");

        trash.MapGet("/", async (ITaskRepository repository, TaskViews views, CancellationToken ct) =>
            await views.ToDtosAsync(await repository.GetTrashedAsync(ct), ct));

        // Дело ленты без своей категории возвращается в «неразобранное»
        trash.MapPost("/{id:int}/restore", async (int id, TaskService service, TaskViews views, CancellationToken ct) =>
            await views.ToDtoAsync(await service.RestoreAsync(id, ct), ct));

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
