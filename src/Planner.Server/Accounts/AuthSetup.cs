using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.RateLimiting;
using Planner.Core.Accounts;
using Planner.Server.Data.Accounts;
using Planner.Server.Data.Storage;

namespace Planner.Server.Accounts;

/// <summary>Утверждения в cookie входа (docs/specs/accounts.md, S1).</summary>
public static class PlannerClaims
{
    public const string UserId = "uid";
    public const string SessionId = "sid";
    public const string Admin = "admin";
    public const string AdminPolicy = "Admin";
}

/// <summary>Чей файл данных открывать — пользователь из cookie входа (D3). Без пользователя данные не открываются.</summary>
public sealed class ClaimsCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid UserId =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirst(PlannerClaims.UserId)?.Value, out var id)
            ? id
            : throw new InvalidOperationException("Запрос без пользователя — данные не открываются.");
}

/// <summary>Вход и защита API: cookie, сессии, политики, частота, CSRF, заголовки (S, C).</summary>
public static class AuthSetup
{
    /// <summary>
    /// Только Development (e2e, демо): запросы без входа впускаются как этот пользователь — чтобы сценарии, которые проверяют не вход,
    /// не проходили его каждый раз. В других окружениях сервер с этой настройкой не запускается.
    /// </summary>
    public const string DevAutoLoginKey = "Auth:DevAutoLogin";

    /// <summary>Cookie только по HTTPS (S1) — включается при деплое; на localhost по http — по запросу.</summary>
    public const string RequireHttpsKey = "Auth:RequireHttps";

    /// <summary>Общий лимит API на пользователя в минуту (C).</summary>
    public const string ApiPermitPerMinuteKey = "Auth:ApiPermitPerMinute";

    public const string CodePolicy = "auth-code";
    public const string VerifyPolicy = "auth-verify";
    public const string CsrfHeader = "X-Planner";

    /// <summary>Отметка запроса: сессия отклонена, потому что пользователь отключён (Q5) — ответ 401 скажет об этом.</summary>
    private const string DisabledMark = "planner.disabled";

    public static IServiceCollection AddPlannerAuth(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, ClaimsCurrentUser>();
        services.AddSingleton<CodeHasher>();
        services.AddSingleton<DevMailbox>();
        services.AddSingleton<FolderEmailSender>();
        services.AddSingleton<SmtpEmailSender>();
        services.AddSingleton<IEmailSender>(sp => MailSettings.Mode(sp.GetRequiredService<IConfiguration>()) == MailMode.Smtp
            ? sp.GetRequiredService<SmtpEmailSender>()
            : sp.GetRequiredService<FolderEmailSender>());
        services.AddScoped<AccountService>();
        services.AddScoped<LoginService>();
        services.AddScoped<SessionService>();
        services.AddScoped<InviteService>();
        services.AddScoped<AccountPurger>();
        services.AddHostedService<AccountPurgeWorker>();

        // Ключи шифрования cookie — в keys\data-protection папки данных (иначе после перезапуска все входы недействительны; D1, D5)
        services.AddDataProtection().SetApplicationName("CoveyPlanner");
        services.AddOptions<KeyManagementOptions>().Configure<DataPaths, ILoggerFactory>((options, paths, loggers) =>
            options.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(paths.DataProtectionDirectory), loggers));

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
        {
            options.Cookie.Name = "planner.session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = Session.SlidingLifetime;
            options.SlidingExpiration = true;
            // API: не перенаправлять на страницу входа, а отвечать кодом — клиент сам покажет вход (Q)
            options.Events.OnRedirectToLogin = context => context.HttpContext.Items.ContainsKey(DisabledMark)
                ? Results.Problem(title: "Аккаунт отключён — обратитесь к администратору", statusCode: StatusCodes.Status401Unauthorized,
                    extensions: new Dictionary<string, object?> { ["code"] = "disabled" }).ExecuteAsync(context.HttpContext)
                : SetStatus(context.Response, StatusCodes.Status401Unauthorized);
            options.Events.OnRedirectToAccessDenied = context => SetStatus(context.Response, StatusCodes.Status403Forbidden);
            options.Events.OnValidatePrincipal = ValidateSessionAsync;
        });
        services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure<IConfiguration>((options, configuration) =>
            {
                if (configuration.GetValue(RequireHttpsKey, false))
                {
                    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                }
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(PlannerClaims.AdminPolicy, policy => policy.RequireClaim(PlannerClaims.Admin, "1"));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, ct) => new ValueTask(WriteTooManyAsync(context.HttpContext,
                context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry) ? retry : TimeSpan.FromMinutes(1), codeRequest: false));
            options.AddPolicy(CodePolicy, http => FixedWindow(Address(http), 20, TimeSpan.FromHours(1)));
            options.AddPolicy(VerifyPolicy, http => FixedWindow(Address(http), 50, TimeSpan.FromHours(1)));
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
                http.Request.Path.StartsWithSegments("/api")
                    ? FixedWindow(http.User.FindFirst(PlannerClaims.UserId)?.Value ?? Address(http),
                        http.RequestServices.GetRequiredService<IConfiguration>().GetValue(ApiPermitPerMinuteKey, 600), TimeSpan.FromMinutes(1))
                    : RateLimitPartition.GetNoLimiter("static"));
        });

        return services;
    }

    /// <summary>Порядок важен: вход → (Development) автовход → права → частота → CSRF.</summary>
    public static WebApplication UsePlannerAuth(this WebApplication app)
    {
        var autoLogin = app.Configuration[DevAutoLoginKey];
        if (!string.IsNullOrWhiteSpace(autoLogin) && !app.Environment.IsDevelopment())
        {
            throw new InvalidOperationException($"{DevAutoLoginKey} — только для окружения Development.");
        }

        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers["Referrer-Policy"] = "same-origin";
            headers.XFrameOptions = "DENY";
            await next();
        });

        app.UseAuthentication();
        if (!string.IsNullOrWhiteSpace(autoLogin))
        {
            app.Use((context, next) => AutoLoginAsync(context, next, autoLogin));
        }

        app.UseAuthorization();
        app.UseRateLimiter();
        app.Use(CsrfAsync);
        return app;
    }

    /// <summary>Вошедший пользователь: новая сессия и cookie (L7).</summary>
    public static async Task<ClaimsPrincipal> SignInAsync(HttpContext context, User user, Session session)
    {
        List<Claim> claims =
        [
            new(PlannerClaims.UserId, user.Id.ToString()),
            new(PlannerClaims.SessionId, session.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
        ];
        if (user.IsAdmin)
        {
            claims.Add(new Claim(PlannerClaims.Admin, "1"));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties { IsPersistent = true });
        context.User = principal;
        return principal;
    }

    public static Guid? UserIdOf(ClaimsPrincipal user) => Guid.TryParse(user.FindFirst(PlannerClaims.UserId)?.Value, out var id) ? id : null;

    public static Guid? SessionIdOf(ClaimsPrincipal user) => Guid.TryParse(user.FindFirst(PlannerClaims.SessionId)?.Value, out var id) ? id : null;

    /// <summary>429 с понятным текстом; codeRequest — частота кодов по почте (L5), иначе — частота запросов вообще.</summary>
    public static Task WriteTooManyAsync(HttpContext context, TimeSpan retryAfter, bool codeRequest = true)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalMinutes));
        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Results.Problem(
            title: !codeRequest ? $"Слишком много запросов — подождите {seconds} с"
                : seconds < 120 ? $"Новый код можно запросить через {seconds} с"
                : $"Слишком много попыток, попробуйте через {minutes} мин",
            statusCode: StatusCodes.Status429TooManyRequests,
            extensions: new Dictionary<string, object?> { ["code"] = "too-many", ["retryAfterSeconds"] = seconds }).ExecuteAsync(context);
    }

    /// <summary>Каждый запрос: сессия не отозвана, не истекла, пользователь есть (S3). Иначе — как без входа.</summary>
    private static async Task ValidateSessionAsync(CookieValidatePrincipalContext context)
    {
        var userId = context.Principal is null ? null : UserIdOf(context.Principal);
        var sessionId = context.Principal is null ? null : SessionIdOf(context.Principal);
        var sessions = context.HttpContext.RequestServices.GetRequiredService<SessionService>();
        var check = userId is null || sessionId is null ? SessionCheck.Invalid : await sessions.ValidateAsync(sessionId.Value, userId.Value);
        if (check != SessionCheck.Ok)
        {
            if (check == SessionCheck.Disabled)
            {
                context.HttpContext.Items[DisabledMark] = true;
            }

            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }

    private static async Task AutoLoginAsync(HttpContext context, Func<Task> next, string email)
    {
        var path = context.Request.Path;
        if (context.User.Identity?.IsAuthenticated != true && path.StartsWithSegments("/api")
            && !path.StartsWithSegments("/api/auth/code") && !path.StartsWithSegments("/api/auth/verify"))
        {
            var services = context.RequestServices;
            var accounts = services.GetRequiredService<AccountsDbContext>();
            var normalized = User.NormalizeEmail(email);
            var user = accounts.Users.FirstOrDefault(u => u.Email == normalized)
                       ?? await services.GetRequiredService<AccountService>().CreateUserAsync(normalized, isAdmin: false);
            var session = await services.GetRequiredService<SessionService>().CreateAsync(user.Id, context.Request.Headers.UserAgent);
            await SignInAsync(context, user, session);
        }

        await next();
    }

    /// <summary>
    /// CSRF (C): изменяющий запрос к API — только с заголовком X-Planner (чужая страница не может его поставить без CORS, а CORS не включён)
    /// и, если браузер прислал Origin, — со своего адреса.
    /// </summary>
    private static async Task CsrfAsync(HttpContext context, Func<Task> next)
    {
        var request = context.Request;
        if (request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method)
            && !HttpMethods.IsOptions(request.Method))
        {
            var origin = request.Headers.Origin.ToString();
            var foreign = origin.Length > 0
                          && !(Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                               && string.Equals(uri.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase)
                               && string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase));
            if (request.Headers[CsrfHeader] != "1" || foreign)
            {
                await Results.Problem(title: "Запрос отклонён: нет заголовка X-Planner или чужой адрес (защита от подделки запросов).",
                    statusCode: StatusCodes.Status400BadRequest).ExecuteAsync(context);
                return;
            }
        }

        await next();
    }

    private static Task SetStatus(HttpResponse response, int status)
    {
        response.StatusCode = status;
        return Task.CompletedTask;
    }

    private static string Address(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static RateLimitPartition<string> FixedWindow(string key, int permit, TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permit,
            Window = window,
            QueueLimit = 0,
        });

    /// <summary>Почтовый ящик разработки виден только в режиме Folder и только с этого компьютера, не через прокси (M2).</summary>
    public static bool IsDevMailboxAvailable(HttpContext context)
    {
        var headers = context.Request.Headers;
        return MailSettings.Mode(context.RequestServices.GetRequiredService<IConfiguration>()) == MailMode.Folder
               && context.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip)
               && !headers.ContainsKey("X-Forwarded-For") && !headers.ContainsKey("Forwarded") && !headers.ContainsKey("X-Real-IP");
    }
}
