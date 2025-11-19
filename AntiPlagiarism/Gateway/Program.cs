using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Для минимального API + OpenAPI
builder.Services.AddOpenApi(); // встроенный OpenAPI в .NET 9
builder.Services.AddEndpointsApiExplorer();

// HttpClient для общения с другими сервисами
builder.Services.AddHttpClient("FileStoringService", client =>
{
    client.BaseAddress = new Uri("http://localhost:5001"); // TODO: вынести в конфиг / env
});

builder.Services.AddHttpClient("FileAnalysisService", client =>
{
    client.BaseAddress = new Uri("http://localhost:5002"); // TODO: вынести в конфиг / env
});

var app = builder.Build();

// OpenAPI (Swagger JSON по адресу /openapi/v1.json)
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(); // вместо UseSwagger/UseSwaggerUI
}

app.UseHttpsRedirection();

// Health-check
app.MapGet("/health", () => Results.Ok("Gateway OK"))
    .WithName("GatewayHealth")
    .WithTags("Health");

// Загрузка работы студентом (через Gateway)
app.MapPost("/works/upload", async (
        HttpRequest request,
        [FromServices] IHttpClientFactory httpClientFactory) =>
    {
        // TODO: спроксировать multipart/form-data запрос в FileStoringService
        return Results.Ok(new
        {
            Message = "Upload endpoint (stub in Gateway)",
            Note = "Здесь будет проксирование в FileStoringService"
        });
    })
    .WithName("UploadWork")
    .WithTags("Works");

// Получение отчетов по работе
app.MapGet("/works/{workId:int}/reports", async (
        int workId,
        [FromServices] IHttpClientFactory httpClientFactory) =>
    {
        // TODO: запросить отчеты в FileAnalysisService
        return Results.Ok(new
        {
            WorkId = workId,
            Message = "Reports endpoint (stub in Gateway)",
            Note = "Здесь будет запрос в FileAnalysisService"
        });
    })
    .WithName("GetWorkReports")
    .WithTags("Reports");

app.Run();