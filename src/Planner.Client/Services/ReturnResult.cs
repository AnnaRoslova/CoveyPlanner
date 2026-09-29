namespace Planner.Client.Services;

/// <summary>
/// Чем закончилась операция, которая может вернуть дело в ленту:
/// восстановление из корзины, удаление цели, удаление роли.
/// </summary>
public enum ReturnResult
{
    Done,

    /// <summary>У дела нет своей категории — спросить категорию и повторить с categoryId.</summary>
    NeedsCategory,

    Failed,
}
