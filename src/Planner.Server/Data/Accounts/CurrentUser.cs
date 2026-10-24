namespace Planner.Server.Data.Accounts;

/// <summary>Чей файл данных открывать в этом запросе (docs/specs/accounts.md, D3).</summary>
public interface ICurrentUser
{
    Guid UserId { get; }
}
