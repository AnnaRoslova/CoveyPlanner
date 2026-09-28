namespace Planner.Core.Enums;

/// <summary>Где сейчас находится дело.</summary>
public enum TaskState
{
    /// <summary>Лента «Когда-нибудь»: без даты, в своей категории.</summary>
    Someday = 0,

    /// <summary>Запланировано: на день (приоритеты или сетка) или на неделю через цель.</summary>
    Planned = 1,

    /// <summary>Выполнено (остаётся на своём месте в плане).</summary>
    Done = 2,

    /// <summary>В корзине.</summary>
    Trashed = 3,
}
