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
public sealed class DragDropService(IJSRuntime js, PlannerState state, WindowHost windows) : IAsyncDisposable
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
    public const string TrashZone = "trash";     // значок корзины внизу ленты
    public const string GoalZone = "goal";       // строка цели: засчитать дело и в эту цель

    /// <summary>data-key зоны «неразобранное» (дела ленты без категории).</summary>
    public const string UnsortedKey = "unsorted";

    /// <summary>Категория ленты: дела ленты, дела из плана (возврат в ленту), цели (цель → мысль).</summary>
    public const string SomedayAccepts = SomedayItem + " " + DayItem + " " + GoalItem;

    /// <summary>День: дела из ленты и других дней, цели (цель → дело-«камень» в этот день).</summary>
    public const string DayAccepts = SomedayItem + " " + DayItem + " " + GoalItem;

    /// <summary>Роль: её и чужие цели, дела из ленты и из дней (дело → цель недели).</summary>
    public const string RoleAccepts = GoalItem + " " + SomedayItem + " " + DayItem;

    /// <summary>Список ролей: только роли (порядок).</summary>
    public const string RolesAccepts = RoleItem;

    /// <summary>Строка цели: дела из дней и из ленты — засчитать и в эту цель.</summary>
    public const string GoalAccepts = SomedayItem + " " + DayItem;

    /// <summary>Стрелки недели: листают во время перетаскивания дела из ленты или из дня (цели привязаны к своей неделе).</summary>
    public const string FlipAccepts = SomedayItem + " " + DayItem;

    /// <summary>Корзина: дела ленты и плана, цели — удалить, как из окна (с «Вернуть»).</summary>
    public const string TrashAccepts = SomedayItem + " " + DayItem + " " + GoalItem;

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

    /// <summary>Ключ зоны ленты (data-key): Id категории или <see cref="UnsortedKey"/> — «неразобранное» (null).</summary>
    private static bool TryFeedKey(string? key, out int? categoryId)
    {
        categoryId = null;
        if (key == UnsortedKey)
        {
            return true;
        }

        if (int.TryParse(key, out var id))
        {
            categoryId = id;
            return true;
        }

        return false;
    }

    /// <summary>Неделя, с которой начали листать во время перетаскивания (null — не листали).</summary>
    private DateOnly? _flipStartWeek;

    /// <summary>
    /// Во время перетаскивания дела задержали над «‹» или «›» в шапке недели — листаем (direction: −1 / +1).
    /// Дело держим, чтобы его можно было бросить в день новой недели.
    /// </summary>
    [JSInvokable]
    public async Task OnFlipWeek(int direction, int id)
    {
        _flipStartWeek ??= state.WeekStart;
        state.KeepWhileDragging(id);
        await state.GoToWeekAsync(state.WeekStart.AddDays(7 * Math.Sign(direction)));
    }

    /// <summary>Перетаскивание после листания отменили (Esc или бросили мимо) — неделя возвращается.</summary>
    [JSInvokable]
    public async Task OnFlipCancel()
    {
        if (_flipStartWeek is { } week)
        {
            _flipStartWeek = null;
            state.KeepWhileDragging(null);
            await state.GoToWeekAsync(week);
        }
    }

    [JSInvokable]
    public async Task OnDrop(DropEvent drop)
    {
        try
        {
            await HandleDropAsync(drop);
        }
        finally
        {
            // Бросили после листания — остаёмся на новой неделе
            _flipStartWeek = null;
            state.KeepWhileDragging(null);
        }
    }

    private async Task HandleDropAsync(DropEvent drop)
    {
        switch (drop)
        {
            // Лента или день → лента: порядок в категории, другая категория, возврат из плана
            case { Kind: SomedayItem or DayItem, Zone: SomedayZone } when TryFeedKey(drop.Key, out var categoryId):
                await state.MoveTaskToFeedAsync(drop.Id, categoryId, drop.Index);
                break;

            // Лента или день → день: запланировать, перенести на другой день, порядок в дне
            case { Kind: SomedayItem or DayItem, Zone: DayZone }
                when DateOnly.TryParseExact(drop.Key, "yyyy-MM-dd", out var date):
                await state.MoveTaskToDayAsync(drop.Id, date, drop.Index);
                break;

            // Лента или день → строка цели: дело засчитывается и в эту цель
            case { Kind: SomedayItem or DayItem, Zone: GoalZone } when int.TryParse(drop.Key, out var goalId):
                await state.LinkTaskToGoalAsync(drop.Id, goalId);
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
            case { Kind: GoalItem, Zone: SomedayZone } when TryFeedKey(drop.Key, out var categoryId):
                await state.MoveGoalToFeedAsync(drop.Id, categoryId, drop.Index);
                break;

            // Запись → корзина: удалить (как из окна, с «Вернуть»)
            case { Kind: SomedayItem or DayItem, Zone: TrashZone }:
                await state.TrashTaskAsync(drop.Id);
                break;

            case { Kind: GoalItem, Zone: TrashZone }:
                await windows.DeleteGoalAsync(state, drop.Id); // постоянную — с вопросом
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
