using System.Net.Http.Json;
using Planner.Shared;

namespace Planner.Client.Services;

/// <summary>
/// Кто вошёл (docs/specs/accounts.md, L, Q). Без входа — экран входа вместо планировщика. Сессия кончилась посреди работы (401) —
/// окно входа поверх экрана без перезагрузки страницы: очередь несохранённого живёт в памяти и ждёт (<see cref="WaitForReloginAsync"/>).
/// Вошли тем же пользователем — очередь уходит; другим — не уходит, страница перезагружается (Q3).
/// </summary>
public sealed class AuthState(HttpClient http)
{
    private TaskCompletionSource _relogin = Done();

    public MeDto? Me { get; private set; }

    /// <summary>Узнали, вошёл ли пользователь (до этого не показываем ни вход, ни планировщик).</summary>
    public bool IsChecked { get; private set; }

    /// <summary>Сессия кончилась посреди работы — окно входа поверх экрана (Q1, Q4).</summary>
    public bool NeedsRelogin { get; private set; }

    /// <summary>Почему вышли: «disabled» — аккаунт отключён администратором (Q5); null — сессия кончилась.</summary>
    public string? Reason { get; private set; }

    /// <summary>Поверх окна входа вошли другим пользователем — несохранённое прежнего не отправляется (Q3).</summary>
    public bool AccountChanged { get; private set; }

    public event Action? Changed;

    public async Task InitializeAsync()
    {
        try
        {
            using var response = await http.GetAsync("api/auth/me");
            Me = response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<MeDto>() : null;
        }
        catch (HttpRequestException)
        {
            Me = null;
        }

        IsChecked = true;
        Changed?.Invoke();
    }

    /// <summary>Сервер ответил 401: показать окно входа и ждать, пока тот же пользователь войдёт снова.</summary>
    public Task WaitForReloginAsync(string? reason = null)
    {
        Reason = reason ?? Reason;
        if (!NeedsRelogin)
        {
            NeedsRelogin = true;
            _relogin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Changed?.Invoke();
        }

        return _relogin.Task;
    }

    /// <summary>Вход удался; true — это тот же пользователь (или первый вход), и ожидающие запросы можно повторить.</summary>
    public bool SignedIn(MeDto me)
    {
        if (NeedsRelogin && Me is not null && Me.UserId != me.UserId)
        {
            AccountChanged = true; // ожидающие запросы так и не повторятся — страница перезагрузится
            Me = me;
            Changed?.Invoke();
            return false;
        }

        Me = me;
        NeedsRelogin = false;
        Reason = null;
        _relogin.TrySetResult();
        Changed?.Invoke();
        return true;
    }

    /// <summary>«Выйти» / «Выйти на всех устройствах» (S4); дальше страницу перезагружает вызывающий.</summary>
    public async Task LogoutAsync(bool everywhere)
    {
        try
        {
            using var response = await http.PostAsync(everywhere ? "api/auth/logout-all" : "api/auth/logout", null);
        }
        catch (HttpRequestException)
        {
            // Нет связи — cookie останется, но выход всё равно покажем; на сервере сессию можно закрыть с другого устройства
        }

        Me = null;
    }

    /// <summary>Код из ответа 401 (ProblemDetails.code), если сервер его прислал.</summary>
    public static async Task<string?> ReasonOfAsync(HttpResponseMessage response)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync();
            return text.Length == 0 ? null
                : System.Text.Json.JsonDocument.Parse(text).RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static TaskCompletionSource Done()
    {
        var done = new TaskCompletionSource();
        done.SetResult();
        return done;
    }
}
