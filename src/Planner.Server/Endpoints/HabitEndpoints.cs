using Planner.Core.Services;
using Planner.Shared;

namespace Planner.Server.Endpoints;

/// <summary>
/// Привычки (постоянные серии пилы; id привычки — id серии) и каникулы. Скрепки считаются по выполненным занятиям
/// (одним запросом); GET /api/habits только читает — ничего не создаёт (в отличие от /api/week, который заполняет
/// недели постоянных целей). Создать привычку — добавить постоянную цель в пилу; отдельного POST нет.
/// </summary>
public static class HabitEndpoints
{
    public static void MapHabitEndpoints(this RouteGroupBuilder api)
    {
        var habits = api.MapGroup("/habits");

        // today — сегодняшняя дата у пользователя (по умолчанию — на сервере)
        habits.MapGet("/", async (DateOnly? today, HabitService service, CancellationToken ct) =>
            await GetAllAsync(service, today ?? service.Today, ct));

        // Настройки банки: стартовое число и размер
        habits.MapPut("/{id:int}", async (int id, UpdateHabitRequest request, DateOnly? today, HabitService service, CancellationToken ct) =>
        {
            await service.UpdateJarAsync(id, request.StartCount, request.JarSize, ct);
            return (await service.GetAsync(id, today ?? service.Today, ct)).ToDto();
        });

        // Только из архива; удаляется счёт (банка, каникулы привычки) — цель, серия и дела остаются
        habits.MapDelete("/{id:int}", async (int id, HabitService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        // «+1 сегодня»: занятие цели этой недели на сегодня — отмеченное
        habits.MapPost("/{id:int}/plus-one", async (int id, PlusOneRequest request, HabitService service, CancellationToken ct) =>
        {
            var (goal, session) = await service.PlusOneAsync(id, request.Today, ct);
            return new PlusOneResultDto(session.ToDto(), goal.ToDto(), (await service.GetAsync(id, request.Today, ct)).ToDto());
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
