using Planner.Core.Enums;
using Planner.Core.Services;
using Planner.Shared;

namespace Planner.Server.Endpoints;

/// <summary>«Выполненное»: выполненные дела из ленты по категориям (только чтение).</summary>
public static class DoneEndpoints
{
    public const string NoCategoryName = "без категории";

    public static void MapDoneEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/done", async (DoneArchiveService service, CancellationToken ct) =>
            (await service.GetAsync(ct)).Select(g => new DoneGroupDto(
                    g.Category?.Id,
                    g.Category?.Name ?? NoCategoryName,
                    g.Category?.Emoji,
                    g.Years.Select(y => new DoneYearDto(y.Year, y.Count)).ToList(),
                    g.Entries.Select(e => new DoneTaskDto(
                            e.Task.ToDto(),
                            e.Date,
                            e.Project.ToDto(),
                            e.Weeks,
                            e.Steps.Where(s => s.State != TaskState.Trashed).Select(s => s.ToStepDto()).ToList()))
                        .ToList()))
                .ToList());
    }
}
