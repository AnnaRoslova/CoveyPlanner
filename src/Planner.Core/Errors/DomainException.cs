namespace Planner.Core.Errors;

/// <summary>Нарушено правило предметной области (API отвечает 400).</summary>
public class DomainException(string message) : Exception(message);

/// <summary>Сущность не найдена (API отвечает 404).</summary>
public class NotFoundException(string entity, int id) : Exception($"{entity} с Id={id} не найден(а).");

/// <summary>
/// Дело нужно вернуть в ленту, а категории у него нет (создано в плане или категорию удалили).
/// API отвечает 409 с кодом «category-required» — клиент спрашивает категорию и повторяет запрос.
/// </summary>
public class CategoryRequiredException()
    : Exception("Выберите категорию, в которую вернуть дело.")
{
    public const string Code = "category-required";
}
