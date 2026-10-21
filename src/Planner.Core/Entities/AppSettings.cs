using Planner.Core.Errors;
using Planner.Core.Thoughts;

namespace Planner.Core.Entities;

/// <summary>Настройки приложения на сервере — одна строка: срок «давно лежат» (batch-ux.md, BS2) и миссия (BM).</summary>
public class AppSettings
{
    public const int SingleId = 1;

    public int Id { get; private set; } = SingleId;

    /// <summary>Срок «давно лежат», месяцев (1–36, по умолчанию 3).</summary>
    public int StaleMonths { get; private set; } = StaleRules.DefaultMonths;

    /// <summary>«Моя миссия» — свободный текст до 5000 знаков, переносы строк сохраняются; null — не написана (BM1).</summary>
    public string? Mission { get; private set; }

    /// <summary>Блок «✦ Моя миссия» вверху левой панели (BM2), по умолчанию выключен.</summary>
    public bool ShowMissionInPanel { get; private set; }

    public static AppSettings CreateDefault() => new();

    public void SetMission(string? mission) => Mission = TextRules.Optional(mission, TextRules.MissionMaxLength, "Миссия");

    public void SetShowMissionInPanel(bool show) => ShowMissionInPanel = show;

    public void SetStaleMonths(int months)
    {
        if (months is < StaleRules.MinMonths or > StaleRules.MaxMonths)
        {
            throw new DomainException($"Срок — от {StaleRules.MinMonths} до {StaleRules.MaxMonths} месяцев.");
        }

        StaleMonths = months;
    }
}
