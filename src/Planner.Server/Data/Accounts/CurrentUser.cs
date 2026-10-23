namespace Planner.Server.Data.Accounts;

/// <summary>Чей файл данных открывать в этом запросе (docs/specs/accounts.md, D3).</summary>
public interface ICurrentUser
{
    Guid UserId { get; }
}

/// <summary>Что узнали о хранилище при запуске: администратор (первый по дате регистрации).</summary>
public sealed class StorageState
{
    public Guid? AdminId { get; set; }
}

/// <summary>
/// Пока входа нет (этап 1 плана), все запросы — от администратора: приложение работает как раньше, но данные уже в его файле.
/// Администратора нет (хранилище не подготовлено) — исключение, а не «чей-то» файл.
/// </summary>
public sealed class AdminAsCurrentUser(StorageState state) : ICurrentUser
{
    public Guid UserId => state.AdminId ?? throw new InvalidOperationException("Хранилище не подготовлено: нет администратора.");
}
