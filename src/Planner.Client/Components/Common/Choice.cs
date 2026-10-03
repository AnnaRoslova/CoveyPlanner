namespace Planner.Client.Components.Common;

/// <summary>Пункт списка в <see cref="ChoiceField{TValue}"/>: значение, подпись и необязательные цветная точка или эмодзи.</summary>
public sealed record Choice<TValue>(TValue Value, string Label, string? Color = null, string? Emoji = null);
