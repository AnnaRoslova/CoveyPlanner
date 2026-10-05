namespace Planner.Core.Enums;

/// <summary>Вид копилки.</summary>
public enum HabitKind
{
    /// <summary>Привычка: норма на неделю, серии недель и месяцев, каникулы, «+1 сегодня».</summary>
    Habit,

    /// <summary>Коллекция (книги, проекты, поездки): без нормы и серий; полка записей, необязательная цель на год.</summary>
    Collection,
}
