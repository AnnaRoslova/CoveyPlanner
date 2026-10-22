namespace Planner.Client.Components.Common;

/// <summary>Где рисуется декор темы «Пять колец» (GorinDecor).</summary>
public enum GorinPlace
{
    /// <summary>Символы SVG — один раз на странице.</summary>
    Defs,

    /// <summary>Неделя: облако, солнце с горами за подписью недели, лепестки.</summary>
    Week,

    /// <summary>Лента: лепестки.</summary>
    Feed,

    /// <summary>Конец левой панели: ветвь сакуры.</summary>
    LeftFoot,

    /// <summary>Конец ленты: волна дыма и ветвь сакуры.</summary>
    FeedFoot,
}
