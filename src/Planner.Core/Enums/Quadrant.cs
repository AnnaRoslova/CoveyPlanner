namespace Planner.Core.Enums;

/// <summary>Квадрант матрицы Кови (важность × срочность).</summary>
public enum Quadrant
{
    /// <summary>I — важно и срочно.</summary>
    UrgentImportant = 1,

    /// <summary>II — важно, не срочно.</summary>
    NotUrgentImportant = 2,

    /// <summary>III — срочно, не важно.</summary>
    UrgentNotImportant = 3,

    /// <summary>IV — не срочно и не важно.</summary>
    NotUrgentNotImportant = 4,
}
