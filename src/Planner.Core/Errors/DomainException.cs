namespace Planner.Core.Errors;

/// <summary>Нарушено правило предметной области (API отвечает 400).</summary>
public class DomainException(string message) : Exception(message);

/// <summary>Сущность не найдена (API отвечает 404).</summary>
public class NotFoundException(string entity, int id) : Exception($"{entity} с Id={id} не найден(а).");
