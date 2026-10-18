using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FoodQueue.Api.Common;

public sealed class AppExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        if (exception is not AppException app)
        {
            return false;
        }

        http.Response.StatusCode = app.Status;
        var details = new ProblemDetails
        {
            Status = app.Status,
            Title = app.Message,
            Detail = app.Message,
            Extensions = { ["code"] = app.Code, ["message"] = app.Message },
        };
        foreach (var (key, value) in app.Extensions)
        {
            details.Extensions[key] = value;
        }

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            Exception = app,
            ProblemDetails = details,
        });
    }
}
