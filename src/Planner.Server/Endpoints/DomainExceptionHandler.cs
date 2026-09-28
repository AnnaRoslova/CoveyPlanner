using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Planner.Core.Errors;
using Planner.Shared;

namespace Planner.Server.Endpoints;

/// <summary>
/// Ошибки домена → 400, «не найдено» → 404, «нужна категория» → 409 с code = category-required.
/// Формат — ProblemDetails, в title текст для пользователя.
/// </summary>
public class DomainExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var status = exception switch
        {
            NotFoundException => StatusCodes.Status404NotFound,
            CategoryRequiredException => StatusCodes.Status409Conflict,
            DomainException => StatusCodes.Status400BadRequest,
            _ => (int?)null,
        };

        if (status is null)
        {
            return false;
        }

        var details = new ProblemDetails { Status = status, Title = exception.Message };
        if (exception is CategoryRequiredException)
        {
            details.Extensions["code"] = ProblemCodes.CategoryRequired;
        }

        context.Response.StatusCode = status.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = details,
        });
    }
}
