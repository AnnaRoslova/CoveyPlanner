using Planner.Core.Abstractions;
using Planner.Core.Entities;
using Planner.Core.Sorting;

namespace Planner.Core.Services;

/// <summary>
/// Серии целей: цели постоянных серий («каждую неделю») появляются на неделях, когда неделю открывают;
/// занятие, перенесённое на другую неделю, попадает в цель той же серии (или в новую — её копию).
/// Сам не сохраняет изменения, кроме <see cref="EnsureWeeksAsync"/>.
/// </summary>
public class GoalSeriesService(
    IGoalSeriesRepository series, IGoalRepository goals, GoalSessions sessions, IUnitOfWork unitOfWork)
{
    // Цели, созданные в этом запросе и ещё не сохранённые: запросы к базе их не видят
    private readonly List<WeeklyGoal> _pending = [];

    /// <summary>
    /// Создать цели постоянных серий на все ещё не созданные недели до <paramref name="upTo"/> включительно
    /// (вызывается, когда открывают неделю). Без дублей: на каждую неделю — один раз (см. <see cref="GoalSeries.GeneratedThrough"/>).
    /// </summary>
    public async Task EnsureWeeksAsync(DateOnly upTo, CancellationToken ct = default)
    {
        var changed = false;
        foreach (var recurring in await series.GetRecurringAsync(ct))
        {
            changed |= await GenerateAsync(recurring, upTo, ct);
        }

        if (changed)
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Цель недели <paramref name="weekStart"/> для занятия цели <paramref name="source"/>, перенесённого на ту неделю:
    /// цель той же серии, а если её нет — новая копия <paramref name="source"/> (1 раз в неделю, без повтора) в той же серии.
    /// У разовой цели для этого появляется непостоянная серия.
    /// </summary>
    public async Task<WeeklyGoal> GoalForWeekAsync(WeeklyGoal source, DateOnly weekStart, CancellationToken ct = default)
    {
        if (source.Series is { } existing)
        {
            await GenerateAsync(existing, weekStart, ct);
            if (await FindAsync(existing, weekStart, ct) is { } same)
            {
                return same;
            }
        }
        else
        {
            var created = GoalSeries.StartFrom(source, recurring: false);
            series.Add(created);
            source.JoinSeries(created);
        }

        var copy = source.CopyForWeek(weekStart);
        await AddToEndOfRoleAsync(copy, ct);
        return copy;
    }

    /// <summary>Цели постоянной серии на недели до <paramref name="upTo"/>, которых ещё нет. true — серия изменилась.</summary>
    private async Task<bool> GenerateAsync(GoalSeries recurring, DateOnly upTo, CancellationToken ct)
    {
        var weeks = recurring.TakeWeeksToGenerate(upTo);
        foreach (var week in weeks)
        {
            // Цель могла уже появиться иначе (на эту неделю переносили занятие)
            if (await FindAsync(recurring, week, ct) is null)
            {
                // Дни выбраны — занятия сразу встают в эти дни (со временем и длительностью из шаблона)
                var goal = WeeklyGoal.FromSeries(recurring, week);
                await AddToEndOfRoleAsync(goal, ct);
                await sessions.ArrangeByDaysAsync(goal, ct);
            }
        }

        return weeks.Count > 0;
    }

    /// <summary>Цель серии на неделю, в том числе только что созданная (ещё не сохранённая).</summary>
    private async Task<WeeklyGoal?> FindAsync(GoalSeries owner, DateOnly week, CancellationToken ct) =>
        _pending.FirstOrDefault(g => g.Series == owner && g.WeekStart == week)
        ?? (owner.Id == 0 ? null : await goals.FindInSeriesAsync(owner.Id, week, ct));

    private async Task AddToEndOfRoleAsync(WeeklyGoal goal, CancellationToken ct)
    {
        // Соседи — и сохранённые, и только что созданные здесь же (у них ещё нет Id)
        var siblings = (await goals.GetForRoleAsync(goal.RoleId, goal.WeekStart, ct))
            .Concat(_pending.Where(g => g.RoleId == goal.RoleId && g.WeekStart == goal.WeekStart))
            .OrderBy(g => g.SortOrder)
            .ToList();
        SortOrdering.Place(siblings, goal, int.MaxValue);
        goals.Add(goal);
        _pending.Add(goal);
    }
}
