using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.API.Infrastructure;

/// <summary>Переводит исключения предметной области и БД в ответы ProblemDetails с понятным текстом.</summary>
public class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Не найдено"),
            InsufficientStockException => (StatusCodes.Status409Conflict, "Недостаточно остатка"),
            DomainException => (StatusCodes.Status400BadRequest, "Недопустимая операция"),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Остаток изменён другим пользователем"),
            DbUpdateException => (StatusCodes.Status409Conflict, "Конфликт данных"),
            _ => (0, string.Empty)
        };

        if (status == 0)
            return false; // необработанные ошибки — стандартный ответ 500

        var detail = exception switch
        {
            DbUpdateConcurrencyException => "Повторите операцию: остаток успел измениться.",
            DbUpdateException => "Запись нарушает ограничение базы данных (например, дубликат кода).",
            _ => exception.Message
        };

        if (exception is DbUpdateException)
            logger.LogWarning(exception, "Конфликт при сохранении в БД");

        context.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail }
        });
    }
}
