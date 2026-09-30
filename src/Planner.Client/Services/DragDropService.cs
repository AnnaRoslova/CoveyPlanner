using Microsoft.JSInterop;

namespace Planner.Client.Services;

/// <summary>
/// Что сообщает wwwroot/js/dnd.js, когда запись бросили в зону.
/// Kind — что тащили (data-drag), Zone и Key — куда (data-drop, data-key),
/// Index — позиция среди записей зоны без самой перетаскиваемой.
/// </summary>
public sealed record DropEvent(string Kind, int Id, string Zone, string? Key, int Index);

/// <summary>
/// Мост между JS-движком перетаскивания и состоянием экрана:
/// JS следит за указателем и рисует подсказки, а что означает «бросили сюда» — решается здесь.
/// </summary>
public sealed class DragDropService(IJSRuntime js, PlannerState state) : IAsyncDisposable
{
    // Виды перетаскиваемого (data-drag) и зоны (data-drop) — те же строки, что в разметке
    public const string SomedayItem = "someday"; // дело в ленте
    public const string DayItem = "day";         // дело в дне недели
    public const string GoalItem = "goal";       // цель роли на неделю
    public const string RoleItem = "role";       // пользовательская роль

    public const string SomedayZone = "someday"; // категория ленты (список или заголовок)
    public const string DayZone = "day";         // день недели
    public const string RoleZone = "role";       // цели одной роли
    public const string RolesZone = "roles";     // список пользовательских ролей

    /// <summary>Категория ленты: дела ленты, дела из плана (возврат в ленту), цели (цель → мысль).</summary>
    public const string SomedayAccepts = SomedayItem + " " + DayItem + " " + GoalItem;

    /// <summary>День: дела из ленты и других дней, цели (цель → дело-«камень» в этот день).</summary>
    public const string DayAccepts = SomedayItem + " " + DayItem + " " + GoalItem;

    /// <summary>Роль: её и чужие цели, дела из ленты и из дней (дело → цель недели).</summary>
    public const string RoleAccepts = GoalItem + " " + SomedayItem + " " + DayItem;

    /// <summary>Список ролей: только роли (порядок).</summary>
    public const string RolesAccepts = RoleItem;

    private DotNetObjectReference<DragDropService>? _selfRef;
    private IJSObjectReference? _module;

    public async Task InitAsync()
    {
        try
        {
            _module = await js.InvokeAsync<IJSObjectReference>("import", "./js/dnd.js");
            _selfRef = DotNetObjectReference.Create(this);
            await _module.InvokeVoidAsync("init", _selfRef);
        }
        catch (JSException)
        {
            // Без JS перетаскивания просто не будет — остальное работает
        }
    }

    [JSInvokable]
    public async Task OnDrop(DropEvent drop)
    {
        switch (drop)
        {
            // Лента или день → лента: порядок в категории, другая категория, возврат из плана
            case { Kind: SomedayItem or DayItem, Zone: SomedayZone } when int.TryParse(drop.Key, out var categoryId):
                await state.MoveTaskToFeedAsync(drop.Id, categoryId, drop.Index);
                break;

            // Лента или день → день: запланировать, перенести на другой день, порядок в дне
            case { Kind: SomedayItem or DayItem, Zone: DayZone }
                when DateOnly.TryParseExact(drop.Key, "yyyy-MM-dd", out var date):
                await state.MoveTaskToDayAsync(drop.Id, date, drop.Index);
                break;

            // Лента или день → роль: дело становится целью недели
            case { Kind: SomedayItem or DayItem, Zone: RoleZone } when int.TryParse(drop.Key, out var roleId):
                await state.CreateGoalFromTaskAsync(drop.Id, roleId, drop.Index);
                break;

            // Цель → роль: порядок целей или перенос к другой роли
            case { Kind: GoalItem, Zone: RoleZone } when int.TryParse(drop.Key, out var roleId):
                await state.MoveGoalAsync(drop.Id, roleId, drop.Index);
                break;

            // Цель → день: её дело («камень») встаёт в этот день
            case { Kind: GoalItem, Zone: DayZone } when DateOnly.TryParseExact(drop.Key, "yyyy-MM-dd", out var date):
                await state.ScheduleGoalAsync(drop.Id, date, drop.Index);
                break;

            // Цель → лента: цель исчезает, дело встаёт в категорию
            case { Kind: GoalItem, Zone: SomedayZone } when int.TryParse(drop.Key, out var categoryId):
                await state.MoveGoalToFeedAsync(drop.Id, categoryId, drop.Index);
                break;

            // Роль → список ролей: порядок
            case { Kind: RoleItem, Zone: RolesZone }:
                await state.MoveRoleToAsync(drop.Id, drop.Index);
                break;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _selfRef?.Dispose();
        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }
    }
}
