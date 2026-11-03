using PaveLens.Application.Common;
using PaveLens.Domain.Common;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace PaveLens.Api.Infrastructure;

/// <summary>Maps application and domain exceptions to RFC 9457 problem responses.</summary>
internal sealed partial class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            ValidationException validation => new HttpValidationProblemDetails(
                validation.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "La solicitud no es válida."
            },
            NotFoundException notFound => Problem(StatusCodes.Status404NotFound, "Recurso no encontrado.", notFound.Message),
            ConcurrencyConflictException => Problem(StatusCodes.Status409Conflict, "Conflicto de concurrencia.",
                "El recurso cambió mientras se procesaba la solicitud. Vuelve a cargarlo e inténtalo de nuevo."),
            DomainException domain => Problem(StatusCodes.Status409Conflict, "Operación no permitida en el estado actual.", domain.Message),
            ModelUnavailableException unavailable => Problem(StatusCodes.Status503ServiceUnavailable, "Modelo no disponible.", unavailable.Message),
            ModelOutputException output => Problem(StatusCodes.Status502BadGateway, "Respuesta del modelo no válida.", output.Message),
            BadHttpRequestException bad => Problem(bad.StatusCode, "Solicitud incorrecta.", bad.Message),
            _ => Problem(StatusCodes.Status500InternalServerError, "Error interno.", null)
        };

        if (problem.Status >= 500)
        {
            LogServerError(exception, httpContext.Request.Method, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }

    private static ProblemDetails Problem(int status, string title, string? detail) =>
        new() { Status = status, Title = title, Detail = detail };

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled error on {Method} {Path}")]
    private partial void LogServerError(Exception exception, string method, string path);
}
