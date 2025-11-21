using System.Collections.Concurrent;
using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Shared.DTOs;

var builder = WebApplication.CreateBuilder(args);

// OpenAPI
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();

// Простое in-memory хранилище метаданных (для старта; позже заменим на БД)
var worksStorage = new ConcurrentDictionary<int, WorkMetaDto>();
var workIdCounter = 0;

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

var storageRoot = Path.Combine(app.Environment.ContentRootPath, "storage");
Directory.CreateDirectory(storageRoot);

app.MapGet("/health", () => Results.Ok("FileStoringService OK"))
    .WithName("FileStoringHealth")
    .WithTags("Health");

app.MapPost("/files", async (
        HttpContext context,
        [FromServices] ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        if (!context.Request.HasFormContentType)
        {
            return Results.Problem(
                title: "Некорректный тип содержимого",
                detail: "Ожидается multipart/form-data",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var form = await context.Request.ReadFormAsync(cancellationToken);

        var file = form.Files["file"];
        var studentId = form["studentId"].ToString();
        var assignmentId = form["assignmentId"].ToString();

        if (file is null || file.Length == 0)
        {
            return Results.Problem(
                title: "Файл не передан",
                detail: "Поле 'file' обязательно и не может быть пустым",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(studentId) ||
            string.IsNullOrWhiteSpace(assignmentId))
        {
            return Results.Problem(
                title: "Неверные данные",
                detail: "Поля 'studentId' и 'assignmentId' обязательны",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Генерируем workId (в реальной системе это было бы из БД)
        var workId = Interlocked.Increment(ref workIdCounter);
        var submittedAt = DateTime.UtcNow;

        // Формируем безопасное имя файла
        var safeFileName = Path.GetFileName(file.FileName);
        var newFileName = $"{workId}_{assignmentId}_{studentId}_{safeFileName}";
        var filePath = Path.Combine(storageRoot, newFileName);

        try
        {
            await using var stream = File.Create(filePath);
            await file.CopyToAsync(stream, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Ошибка при сохранении файла workId={WorkId}", workId);

            return Results.Problem(
                title: "Ошибка сохранения файла",
                detail: "Не удалось сохранить файл на сервере",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        var meta = new WorkMetaDto
        {
            WorkId = workId,
            AssignmentId = assignmentId,
            StudentId = studentId,
            SubmittedAt = submittedAt,
            FilePath = filePath
        };

        worksStorage[workId] = meta;

        return Results.Ok(meta);
    })
    .Accepts<IFormFile>("multipart/form-data")
    .Produces<WorkMetaDto>(StatusCodes.Status200OK)
    .WithName("UploadFile")
    .WithTags("Files");

app.MapGet("/files/{workId:int}/meta", (
        int workId) =>
    {
        if (!worksStorage.TryGetValue(workId, out var meta))
        {
            return Results.Problem(
                title: "Сдача не найдена",
                detail: $"Сдача с идентификатором {workId} не найдена",
                statusCode: StatusCodes.Status404NotFound);
        }

        return Results.Ok(meta);
    })
    .Produces<WorkMetaDto>(StatusCodes.Status200OK)
    .WithName("GetFileMeta")
    .WithTags("Files");

// (Опционально) GET /files/{workId}/download для FileAnalysisService
// отдавать сам файл.

app.Run();
