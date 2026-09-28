using Planner.Shared;

namespace Planner.Client.Services;

/// <summary>Положение дела в сетке часов: сверху/высота в пикселях и «дорожка» при пересечениях.</summary>
public sealed record GridBlock(TaskDto Task, double Top, double Height, int Lane, int LaneCount);

/// <summary>
/// Раскладка дел одного дня в сетке. Пересекающиеся по времени дела
/// становятся рядом, в колонках-дорожках (как в календарях).
/// </summary>
public static class GridLayout
{
    /// <summary>Высота одного шага сетки в пикселях (синхронно с --slot-height в CSS).</summary>
    public const double SlotHeight = 24;

    public static double MinutesToPixels(int minutes, int slotMinutes) => minutes / (double)slotMinutes * SlotHeight;

    public static IReadOnlyList<GridBlock> Arrange(IEnumerable<TaskDto> tasks, SettingsDto settings)
    {
        var items = tasks
            .Where(t => t.StartTime is not null)
            .Select(t =>
            {
                var start = t.StartTime!.Value.Hour * 60 + t.StartTime.Value.Minute;
                return (Task: t, Start: start, End: start + Math.Max(t.DurationMinutes ?? settings.SlotMinutes, 1));
            })
            .OrderBy(x => x.Start).ThenByDescending(x => x.End)
            .ToList();

        var result = new List<GridBlock>();
        var cluster = new List<(TaskDto Task, int Start, int End, int Lane)>();
        var laneEnds = new List<int>();
        var clusterEnd = int.MinValue;

        void FlushCluster()
        {
            foreach (var c in cluster)
            {
                var top = MinutesToPixels(c.Start - settings.DayStartHour * 60, settings.SlotMinutes);
                var height = MinutesToPixels(c.End - c.Start, settings.SlotMinutes);
                result.Add(new GridBlock(c.Task, top, height, c.Lane, laneEnds.Count));
            }

            cluster.Clear();
            laneEnds.Clear();
        }

        foreach (var item in items)
        {
            // Новая группа, если дело начинается после конца всех дел группы
            if (item.Start >= clusterEnd)
            {
                FlushCluster();
            }

            var lane = laneEnds.FindIndex(end => end <= item.Start);
            if (lane < 0)
            {
                lane = laneEnds.Count;
                laneEnds.Add(item.End);
            }
            else
            {
                laneEnds[lane] = item.End;
            }

            cluster.Add((item.Task, item.Start, item.End, lane));
            clusterEnd = Math.Max(clusterEnd, item.End);
        }

        FlushCluster();
        return result;
    }
}
