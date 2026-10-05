// Помощник сценариев (папка lib — tools/e2e.ps1 её не запускает).
// История копилок «задним числом» — для снимков экрана «Копилки» (как в docs/habits-mockup.html).
// Скрепки — настоящие выполненные дела через API; день создания копилки сдвигается прямо во временной базе
// сценария (PLANNER_DB от tools/e2e.ps1; рабочую базу e2e не трогает).
import { DatabaseSync } from 'node:sqlite';

const pad = n => String(n).padStart(2, '0');
const iso = d => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
const addDays = (d, n) => { const x = new Date(d); x.setDate(x.getDate() + n); return x; };

export async function makeHabitHistory(base, dbPath) {
  if (!dbPath) throw new Error('нет PLANNER_DB — запускайте через tools/e2e.ps1');
  const api = async (method, path, body) => {
    const r = await fetch(new URL(`api/${path}`, base), {
      method, headers: { 'Content-Type': 'application/json' }, body: body ? JSON.stringify(body) : undefined,
    });
    if (!r.ok) throw new Error(`${method} ${path}: ${r.status} ${await r.text()}`);
    return r.status === 204 ? null : r.json();
  };
  const today = new Date();
  const monday = addDays(today, -((today.getDay() + 6) % 7));
  const habits = (await api('GET', `habits?today=${iso(today)}`)).habits;
  const byName = name => habits.find(h => h.name === name);
  const meditation = await api('POST', 'habits', { name: 'Медитация', emoji: '🧘', color: '#c9b8ec', weeklyNorm: 5, jarSize: 30, startCount: 0 });

  // Узор — по неделям от 25 недель назад до текущей: цифра — скрепок за неделю, «_» — до копилки
  const plans = [
    { habit: byName('Тренировки'), weeks: '3333033330033333033333333' + '2' },
    { habit: byName('Чтение'), weeks: '4444444400444440044441444' + '5', vacation: [8, 9] },
    { habit: meditation, weeks: '______5552555005555555552' + '1' },
  ];
  const db = new DatabaseSync(dbPath);
  for (const plan of plans) {
    const first = plan.weeks.search(/\d/);
    for (let w = first; w < plan.weeks.length; w++) {
      const week = addDays(monday, -7 * (plan.weeks.length - 1 - w));
      for (let i = 0; i < Number(plan.weeks[w]); i++) {
        // Скрепки текущей недели — не позже сегодняшнего дня
        const day = addDays(week, i % 7);
        const date = iso(day > today ? today : day);
        const task = await api('POST', 'tasks', { title: plan.habit.name, target: { kind: 'day', date, index: 99 } });
        await api('PUT', `tasks/${task.id}`, { title: task.title, notes: null, roleIds: [], quadrant: null, startTime: null, durationMinutes: null, habitId: plan.habit.id });
        await api('PUT', `tasks/${task.id}/done`, { isDone: true });
      }
    }
    if (plan.vacation) {
      const [a, b] = plan.vacation.map(w => addDays(monday, -7 * (plan.weeks.length - 1 - w)));
      await api('POST', 'vacations', { from: iso(a), to: iso(addDays(b, 6)), habitIds: [plan.habit.id] });
    }
    // Копилку «создали» в понедельник первой недели узора
    const created = addDays(monday, -7 * (plan.weeks.length - 1 - first));
    db.prepare('UPDATE Habits SET CreatedAt = ? WHERE Id = ?').run(`${iso(created)} 08:00:00`, plan.habit.id);
  }
  db.close();
}
