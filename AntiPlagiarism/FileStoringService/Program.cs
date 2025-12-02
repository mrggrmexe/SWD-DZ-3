using FileStoringService.Models;
using FileStoringService.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Shared.DTOs;

var builder = WebApplication.CreateBuilder(args);

// ---------- СЕРВИСЫ ----------

// OpenAPI (встроенный в .NET 9)
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();

// Единый формат ошибок
builder.Services.AddProblemDetails();

// In-memory репозиторий сдач
builder.Services.AddSingleton<IWorkRepository, InMemoryWorkRepository>();

var app = builder.Build();

// ---------- НАСТРОЙКА КОРНЯ ХРАНИЛИЩА ФАЙЛОВ ----------

// 1) ENV: FILE_STORAGE_ROOT
var envStorageRoot = app.Configuration["FILE_STORAGE_ROOT"];

// 2) appsettings.json: "FileStorage:RootPath"
var configStorageRoot = app.Configuration["FileStorage:RootPath"];

// 3) дефолт: {ContentRoot}/storage
var defaultStorageRoot = Path.Combine(app.Environment.ContentRootPath, "storage");

var storageRoot = !string.IsNullOrWhiteSpace(envStorageRoot)
    ? envStorageRoot
    : !string.IsNullOrWhiteSpace(configStorageRoot)
        ? configStorageRoot!
        : defaultStorageRoot;

Directory.CreateDirectory(storageRoot);
app.Logger.LogInformation("File storage root: {StorageRoot}", storageRoot);

// ---------- PIPELINE ----------

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseHttpsRedirection();

// ---------- ENDPOINTS ----------

// Health-check
app.MapGet("/health", () => Results.Ok("FileStoringService OK"))
    .WithName("FileStoringHealth")
    .WithTags("Health");


// POST /files — приём файла и метаданных, возврат WorkMetaDto
app.MapPost("/files", async (
        HttpContext context,
        [FromServices] ILogger<Program> logger,
        [FromServices] IWorkRepository workRepository,
        CancellationToken cancellationToken) =>
    {
        // 1. Проверяем, что запрос multipart/form-data
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

        // 2. Базовая валидация
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

        // 3. Время сдачи — фиксируем сейчас (UTC)
        var submittedAt = DateTime.UtcNow;

        // 4. Создаём Work без Id и FilePath
        var work = new Work
        {
            StudentId = studentId,
            AssignmentId = assignmentId,
            SubmittedAt = submittedAt,
            FilePath = string.Empty // заполним после генерации workId и сохранения файла
        };

        // 5. Сохраняем Work в репозиторий — он генерирует новый Id (workId)
        work = await workRepository.AddAsync(work, cancellationToken);

        // 6. Формируем имя файла на основе work.Id
        var safeFileName = Path.GetFileName(file.FileName);
        var newFileName = $"{work.Id}_{work.AssignmentId}_{work.StudentId}_{safeFileName}";
        var filePath = Path.Combine(storageRoot, newFileName);

        // 7. Сохраняем файл на диск
        try
        {
            await using var stream = File.Create(filePath);
            await file.CopyToAsync(stream, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Ошибка при сохранении файла для workId={WorkId}", work.Id);

            return Results.Problem(
                title: "Ошибка сохранения файла",
                detail: "Не удалось сохранить файл на сервере",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        // 8. Обновляем путь к файлу в сущности
        work.FilePath = filePath;

        // 9. Формируем DTO для ответа
        var meta = new WorkMetaDto
        {
            WorkId = work.Id,
            AssignmentId = work.AssignmentId,
            StudentId = work.StudentId,
            SubmittedAt = work.SubmittedAt,
            FilePath = work.FilePath
        };

        // 10. Возвращаем JSON с workId и метаданными
        return Results.Ok(meta);
    })
    .Accepts<IFormFile>("multipart/form-data")
    .Produces<WorkMetaDto>(StatusCodes.Status200OK)
    .WithName("UploadFile")
    .WithTags("Files");


// GET /files/{workId}/meta — метаданные сдачи
app.MapGet("/files/{workId:int}/meta", async (
        int workId,
        [FromServices] IWorkRepository workRepository,
        CancellationToken cancellationToken) =>
    {
        var work = await workRepository.GetByIdAsync(workId, cancellationToken);
        if (work is null)
        {
            return Results.Problem(
                title: "Сдача не найдена",
                detail: $"Сдача с идентификатором {workId} не найдена",
                statusCode: StatusCodes.Status404NotFound);
        }

        var meta = new WorkMetaDto
        {
            WorkId = work.Id,
            AssignmentId = work.AssignmentId,
            StudentId = work.StudentId,
            SubmittedAt = work.SubmittedAt,
            FilePath = work.FilePath
        };

        return Results.Ok(meta);
    })
    .Produces<WorkMetaDto>(StatusCodes.Status200OK)
    .WithName("GetFileMeta")
    .WithTags("Files");

app.Run();
