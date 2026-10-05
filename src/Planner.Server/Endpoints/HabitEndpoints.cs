using Planner.Core.Services;
using Planner.Shared;

namespace Planner.Server.Endpoints;

/// <summary>
/// Копилки привычек и каникулы. Скрепки считаются по выполненным делам (одним запросом); только чтение —
/// GET /api/habits ничего не создаёт (в отличие от /api/week, который заполняет недели постоянных целей).
/// </summary>
public static class HabitEndpoints
{
    public static void MapHabitEndpoints(this RouteGroupBuilder api)
    {
        var habits = api.MapGroup("/habits");

        // today — сегодняшняя дата у пользователя (по умолчанию — на сервере)
        habits.MapGet("/", async (DateOnly? today, HabitService service, CancellationToken ct) =>
            await GetAllAsync(service, today ?? service.Today, ct));

        habits.MapPost("/", async (SaveHabitRequest request, HabitService service, CancellationToken ct) =>
        {
            var habit = await service.CreateAsync(request.ToFields(), ct);
            return Results.Created($"/api/habits/{habit.Id}", (await service.GetAsync(habit.Id, service.Today, ct)).ToDto());
        });

        habits.MapPut("/{id:int}", async (int id, SaveHabitRequest request, DateOnly? today, HabitService service, CancellationToken ct) =>
        {
            await service.UpdateAsync(id, request.ToFields(), request.IsArchived, ct);
            return (await service.GetAsync(id, today ?? service.Today, ct)).ToDto();
        });

        // Дела и цели остаются — без копилки
        habits.MapDelete("/{id:int}", async (int id, HabitService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        // «+1 сегодня»: выполненное дело с названием копилки в конец сегодняшнего дня
        habits.MapPost("/{id:int}/plus-one", async (int id, PlusOneRequest request, HabitService service, CancellationToken ct) =>
        {
            var task = await service.PlusOneAsync(id, request.Today, ct);
            return new PlusOneResultDto(task.ToDto(), (await service.GetAsync(id, request.Today, ct)).ToDto());
        });

        var vacations = api.MapGroup("/vacations");

        vacations.MapPost("/", async (CreateVacationRequest request, HabitService service, CancellationToken ct) =>
        {
            var vacation = await service.CreateVacationAsync(request.From, request.To, request.HabitIds ?? [], ct);
            return Results.Created($"/api/vacations/{vacation.Id}", vacation.ToDto());
        });

        vacations.MapDelete("/{id:int}", async (int id, HabitService service, CancellationToken ct) =>
        {
            await service.DeleteVacationAsync(id, ct);
            return Results.NoContent();
        });
    }

    private static async Task<HabitsDto> GetAllAsync(HabitService service, DateOnly today, CancellationToken ct) =>
        new(today,
            (await service.GetOverviewAsync(today, ct)).Select(o => o.ToDto()).ToList(),
            (await service.GetVacationsAsync(ct)).Select(v => v.ToDto()).ToList());
}
