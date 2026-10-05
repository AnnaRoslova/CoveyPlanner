namespace Planner.Core.Entities;

/// <summary>
/// Категория ленты, привязанная к коллекции (таблица HabitCategories): выполненное дело из неё само попадает в коллекцию.
/// Категория — не больше чем у одной копилки. Живёт только внутри копилки.
/// </summary>
public class HabitCategory
{
    // Для EF Core
    private HabitCategory()
    {
    }

    public HabitCategory(int categoryId) => CategoryId = categoryId;

    public int CategoryId { get; private set; }
}
