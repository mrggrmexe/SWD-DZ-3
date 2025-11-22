using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Shared.DTOs;

var builder = WebApplication.CreateBuilder(args);

// ---------- СЕРВИСЫ ----------

// OpenAPI (встроенный в .NET 9)
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();

// ProblemDetails, если захочешь отдавать единый формат ошибок
builder.Services.AddProblemDetails();

var app = builder.Build();

// ---------- НАСТРОЙКА КОРНЯ ХРАНИЛИЩА ФАЙЛОВ ----------

// 1) Пробуем взять из ENV: FILE_STORAGE_ROOT
var envStorageRoot = app.Configuration["FILE_STORAGE_ROOT"];

// 2) Если нет ENV, пробуем из appsettings.json: "FileStorage:RootPath"
var configStorageRoot = app.Configuration["FileStorage:RootPath"];

// 3) Дефолт: {ContentRoot}/storage
var defaultStorageRoot = Path.Combine(app.Environment.ContentRootPath, "storage");

// Выбираем первый непустой вариант
var storageRoot = !string.IsNullOrWhiteSpace(envStorageRoot)
    ? envStorageRoot
    : !string.IsNullOrWhiteSpace(configStorageRoot)
        ? configStorageRoot!
        : defaultStorageRoot;

// Создаём папку, если её ещё нет
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

// ---------- IN-MEMORY ХРАНИЛИЩЕ МЕТАДАННЫХ ----------

// workId -> WorkMetaDto
var worksStorage = new ConcurrentDictionary<int, WorkMetaDto>();
var workIdCounter = 0;

// ---------- ENDPOINTS ----------

// Health-check
app.MapGet("/health", () => Results.Ok("FileStoringService OK"))
    .WithName("FileStoringHealth")
    .WithTags("Health");


// POST /files — приём файла и метаданных, возврат WorkMetaDto
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

        // Генерируем workId (в реальной системе — из БД)
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


// GET /files/{workId}/meta — метаданные сдачи
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


// (Опционально) здесь можно добавить GET /files/{workId}/download для отдачи бинарника

app.Run();
