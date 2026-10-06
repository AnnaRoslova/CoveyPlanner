using Planner.Core.Services;
using Planner.Shared;

namespace Planner.Server.Endpoints;

/// <summary>Шаги дела в его окне: список, новый шаг, переименовать, отметить, порядок, удалить.</summary>
public static class StepEndpoints
{
    public static void MapStepEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/tasks/{id:int}/steps", async (int id, StepService service, CancellationToken ct) =>
            (await service.GetAsync(id, ct)).Select(s => s.ToStepDto()).ToList());

        // Новый шаг — в конец списка
        api.MapPost("/tasks/{id:int}/steps", async (int id, StepRequest request, StepService service, CancellationToken ct) =>
        {
            var step = await service.AddAsync(id, request.Title, ct);
            return Results.Created($"/api/steps/{step.Id}", step.ToStepDto());
        });

        // Порядок шагов в списке родителя (все шаги, в нужном порядке)
        api.MapPut("/tasks/{id:int}/steps/order", async (int id, ReorderRequest request, StepService service, CancellationToken ct) =>
        {
            await service.ReorderAsync(id, request.Ids, ct);
            return Results.NoContent();
        });

        var steps = api.MapGroup("/steps");

        steps.MapPut("/{id:int}", async (int id, StepRequest request, StepService service, CancellationToken ct) =>
            (await service.RenameAsync(id, request.Title, ct)).ToStepDto());

        steps.MapPut("/{id:int}/done", async (int id, SetDoneRequest request, StepService service, CancellationToken ct) =>
            (await service.SetDoneAsync(id, request.IsDone, ct)).Task.ToStepDto());

        steps.MapDelete("/{id:int}", async (int id, StepService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ct);
            return Results.NoContent();
        });
    }
}
