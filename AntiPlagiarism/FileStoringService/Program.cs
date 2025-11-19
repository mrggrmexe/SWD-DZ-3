using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// OpenAPI
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();

// TODO: добавить DbContext, конфигурацию хранилища файлов и т.д.

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Health-check
app.MapGet("/health", () => Results.Ok("FileStoringService OK"))
    .WithName("FileStoringHealth")
    .WithTags("Health");

// Загрузка файла (локальный API, к нему будет ходить Gateway)
app.MapPost("/files", async (HttpRequest request) =>
    {
        // TODO:
        // 1. Прочитать multipart/form-data
        // 2. Сохранить файл (локально / S3 / volume)
        // 3. Записать метаданные в БД
        // 4. Вернуть идентификатор работы / файла

        return Results.Ok(new
        {
            Message = "File upload endpoint (stub in FileStoringService)",
            Note = "Здесь будет логика сохранения файла и метаданных"
        });
    })
    .WithName("UploadFile")
    .WithTags("Files");

// Получение метаданных по файлу / работе
app.MapGet("/files/{fileId:int}/meta", (int fileId) =>
    {
        // TODO: достать метаданные из БД
        return Results.Ok(new
        {
            FileId = fileId,
            Message = "Get file meta (stub in FileStoringService)",
            Note = "Здесь будет возврат информации о файле/работе"
        });
    })
    .WithName("GetFileMeta")
    .WithTags("Files");

app.Run();