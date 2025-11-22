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

// ProblemDetails
builder.Services.AddProblemDetails();

// Регистрируем in-memory репозиторий сдач
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

        var submittedAt = DateTime.UtcNow;

        // Формируем безопасное имя файла
        var safeFileName = Path.GetFileName(file.FileName);
        var newFileName = $"{Guid.NewGuid():N}_{assignmentId}_{studentId}_{safeFileName}";
        var filePath = Path.Combine(storageRoot, newFileName);

        try
        {
            await using var stream = File.Create(filePath);
            await file.CopyToAsync(stream, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Ошибка при сохранении файла для студента {StudentId}, assignment {AssignmentId}",
                studentId, assignmentId);

            return Results.Problem(
                title: "Ошибка сохранения файла",
                detail: "Не удалось сохранить файл на сервере",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        // Создаём доменную сущность Work
        var work = new Work
        {
            StudentId = studentId,
            AssignmentId = assignmentId,
            SubmittedAt = submittedAt,
            FilePath = filePath
        };

        // Сохраняем через репозиторий (Id присвоится внутри)
        work = await workRepository.AddAsync(work, cancellationToken);

        // Формируем DTO для ответа
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
