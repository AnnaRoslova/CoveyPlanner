using Planner.Core.Errors;
using Planner.Core.Thoughts;

namespace Planner.Core.Entities;

/// <summary>Настройки приложения на сервере — одна строка (batch-ux.md, BS2; потом — миссия, BM).</summary>
public class AppSettings
{
    public const int SingleId = 1;

    public int Id { get; private set; } = SingleId;

    /// <summary>Срок «давно лежат», месяцев (1–36, по умолчанию 3).</summary>
    public int StaleMonths { get; private set; } = StaleRules.DefaultMonths;

    public static AppSettings CreateDefault() => new();

    public void SetStaleMonths(int months)
    {
        if (months is < StaleRules.MinMonths or > StaleRules.MaxMonths)
        {
            throw new DomainException($"Срок — от {StaleRules.MinMonths} до {StaleRules.MaxMonths} месяцев.");
        }

        StaleMonths = months;
    }
}
