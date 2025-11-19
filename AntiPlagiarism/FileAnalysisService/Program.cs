using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// OpenAPI
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();

// TODO: DbContext для работ и отчетов
// TODO: модели Work / Report

builder.Services.AddHttpClient("FileStoringService", client =>
{
    client.BaseAddress = new Uri("http://localhost:5001"); // TODO: вынести в конфиг / env
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Health-check
app.MapGet("/health", () => Results.Ok("FileAnalysisService OK"))
    .WithName("FileAnalysisHealth")
    .WithTags("Health");

// Запуск анализа работы
app.MapPost("/analyze/{workId:int}", async (
        int workId,
        [FromServices] IHttpClientFactory httpClientFactory) =>
    {
        // TODO:
        // 1. Получить данные/файл из FileStoringService
        // 2. Выполнить алгоритм определения плагиата
        // 3. Сохранить отчет в БД
        // 4. Вернуть краткую информацию об отчете

        return Results.Ok(new
        {
            WorkId = workId,
            Message = "Analyze endpoint (stub in FileAnalysisService)",
            Note = "Здесь будет логика анализа и сохранения отчета"
        });
    })
    .WithName("AnalyzeWork")
    .WithTags("Analysis");

// Получение всех отчетов по работе
app.MapGet("/works/{workId:int}/reports", (int workId) =>
    {
        // TODO: достать отчеты из БД
        return Results.Ok(new
        {
            WorkId = workId,
            Message = "Get reports (stub in FileAnalysisService)",
            Note = "Здесь будет возврат списка отчетов по работе"
        });
    })
    .WithName("GetReportsForWork")
    .WithTags("Reports");

app.Run();