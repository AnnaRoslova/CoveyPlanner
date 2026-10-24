// Помощник сценариев (папка lib — tools/e2e.ps1 её не запускает).
// История привычек «задним числом» — для снимков экрана «Копилки» (как в docs/habits-mockup.html).
// Привычка — постоянная цель пилы: цель создаётся на первой неделе узора, недели открываются по очереди (цели серии
// появляются сами), скрепки — настоящие выполненные занятия. Всё через API, база не правится.
const pad = n => String(n).padStart(2, '0');
const iso = d => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
const addDays = (d, n) => { const x = new Date(d); x.setDate(x.getDate() + n); return x; };

/** Названия привычек истории: Тело — «Тренировки» (🔥14), Ум — «Чтение книг» (каникулы, норма 4 → 3), Дух — «Дыхание» (⚠ пауза · 🔥9). */
export const HISTORY = { training: 'Тренировки', reading: 'Чтение книг', breathing: 'Дыхание' };

export async function makeHabitHistory(base) {
  const api = async (method, path, body) => {
    const r = await fetch(new URL(`api/${path}`, base), {
      method, headers: { 'Content-Type': 'application/json', 'X-Planner': '1' }, body: body ? JSON.stringify(body) : undefined,
    });
    if (!r.ok) throw new Error(`${method} ${path}: ${r.status} ${await r.text()}`);
    return r.status === 204 ? null : r.json();
  };
  const today = new Date();
  const monday = addDays(today, -((today.getDay() + 6) % 7));
  const roles = Object.fromEntries((await api('GET', 'board')).roles.map(r => [r.name, r.id]));

  // Узор — по неделям от 25 недель назад до текущей: цифра — скрепок за неделю, «_» — до привычки.
  // normFrom — с какой недели узора норма другая (правка цели «с этой недели и дальше»)
  const plans = [
    { title: HISTORY.training, role: 'Тело', norm: 3, weeks: '3333033330033333033333333' + '3' },
    { title: HISTORY.reading, role: 'Ум', norm: 4, weeks: '4444444400444440044441444' + '5', vacation: [8, 9], normFrom: [12, 3] },
    { title: HISTORY.breathing, role: 'Дух', norm: 5, weeks: '______5552555005555555552' + '1' },
  ];
  const vacations = [];
  for (const plan of plans) {
    const first = plan.weeks.search(/\d/);
    const weekOf = w => addDays(monday, -7 * (plan.weeks.length - 1 - w));
    const created = await api('POST', 'goals', { roleId: roles[plan.role], weekStart: iso(weekOf(first)), title: plan.title, isRecurring: true });
    const template = norm => ({
      title: plan.title, roleIds: [roles[plan.role]], timesPerWeek: norm, startTime: null, quadrant: null, notes: null,
      durationMinutes: null, allowSeveralPerDay: true, // скрепки текущей недели — не позже сегодня, бывает несколько в день
    });
    await api('PUT', `goals/${created.id}`, template(plan.norm));
    for (let w = first; w < plan.weeks.length; w++) {
      const week = weekOf(w);
      const goal = (await api('GET', `week/${iso(week)}`)).goals.find(g => g.seriesId === created.seriesId);
      if (plan.normFrom?.[0] === w) {
        await api('PUT', `goals/${goal.id}`, template(plan.normFrom[1]));
      }

      for (let i = 0; i < Number(plan.weeks[w]); i++) {
        const day = addDays(week, i % 7);
        const date = iso(day > today ? today : day);
        const session = (await api('POST', `goals/${goal.id}/schedule`, { kind: 'day', date, index: 99 })).task;
        await api('PUT', `tasks/${session.id}/done`, { isDone: true });
      }
    }
    if (plan.vacation) {
      const [a, b] = plan.vacation.map(weekOf);
      vacations.push({ from: iso(a), to: iso(addDays(b, 6)), habitIds: [created.seriesId] });
    }
  }
  for (const vacation of vacations) {
    await api('POST', 'vacations', vacation);
  }
}
