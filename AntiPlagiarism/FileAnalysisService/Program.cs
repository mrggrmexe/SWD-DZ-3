using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Shared.DTOs;

var builder = WebApplication.CreateBuilder(args);

// OpenAPI
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();

// HttpClient к FileStoringService (за метаданными/файлом)
builder.Services.AddHttpClient("FileStoringService", (sp, client) =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var baseUrl = configuration["FILE_STORING_BASE_URL"] ?? "http://localhost:5001";
    client.BaseAddress = new Uri(baseUrl);
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// workId -> WorkMetaDto (мы можем кэшировать метаданные из FileStoringService)
var works = new ConcurrentDictionary<int, WorkMetaDto>();

var reports = new ConcurrentDictionary<int, ReportDetailsDto>();
var reportIdCounter = 0;

app.MapGet("/health", () => Results.Ok("FileAnalysisService OK"))
    .WithName("FileAnalysisHealth")
    .WithTags("Health");

app.MapPost("/analyze/{workId:int}", async (
        int workId,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromServices] ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        // 1. Получаем метаданные работы из FileStoringService (если ещё не кэшировали)
        if (!works.TryGetValue(workId, out var workMeta))
        {
            var fileStoreClient = httpClientFactory.CreateClient("FileStoringService");

            var response = await fileStoreClient.GetAsync(
                $"/files/{workId}/meta",
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return Results.Problem(
                    title: "Сдача не найдена",
                    detail: $"Сдача с идентификатором {workId} отсутствует в FileStoringService",
                    statusCode: StatusCodes.Status404NotFound);
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning(
                    "FileStoringService вернул ошибку при получении meta workId={WorkId}: {Status} {Body}",
                    workId, response.StatusCode, body);

                return Results.Problem(
                    title: "Ошибка FileStoringService",
                    detail: $"Не удалось получить метаданные сдачи (статус {(int)response.StatusCode})",
                    statusCode: StatusCodes.Status502BadGateway);
            }

            workMeta = await response.Content
                .ReadFromJsonAsync<WorkMetaDto>(cancellationToken: cancellationToken)
                       ?? throw new InvalidOperationException(
                           "Пустой ответ при получении метаданных сдачи");

            works[workId] = workMeta;
        }

        // 2. Реализуем простой алгоритм плагиата:
        // есть ли более ранняя сдача этой же работы (assignmentId), но от другого студента?
        var isPlagiarism = works.Values.Any(x =>
            x.AssignmentId == workMeta.AssignmentId &&
            x.StudentId != workMeta.StudentId &&
            x.SubmittedAt < workMeta.SubmittedAt);

        var reportId = Interlocked.Increment(ref reportIdCounter);
        var createdAt = DateTime.UtcNow;

        var report = new ReportDetailsDto
        {
            ReportId = reportId,
            WorkId = workMeta.WorkId,
            AssignmentId = workMeta.AssignmentId,
            StudentId = workMeta.StudentId,
            IsPlagiarism = isPlagiarism,
            Status = "Done",
            CreatedAt = createdAt,
            Details = isPlagiarism
                ? "Обнаружена более ранняя сдача той же работы другим студентом"
                : "Более ранних сдач этой работы другими студентами не обнаружено",
            WordCloudUrl = null // позже сюда можно добавить URL от QuickChart
        };

        reports[reportId] = report;

        // Для удобства сразу отдадим краткий отчёт
        var summary = new ReportSummaryDto
        {
            ReportId = report.ReportId,
            WorkId = report.WorkId,
            StudentId = report.StudentId,
            IsPlagiarism = report.IsPlagiarism,
            Status = report.Status,
            CreatedAt = report.CreatedAt,
            WordCloudUrl = report.WordCloudUrl
        };

        return Results.Ok(summary);
    })
    .Produces<ReportSummaryDto>(StatusCodes.Status200OK)
    .WithName("AnalyzeWork")
    .WithTags("Analysis");

app.MapGet("/assignments/{assignmentId}/reports", (
        string assignmentId) =>
    {
        if (string.IsNullOrWhiteSpace(assignmentId))
        {
            return Results.Problem(
                title: "Некорректный идентификатор задания",
                detail: "assignmentId не может быть пустым",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var result = new AssignmentReportsResponseDto
        {
            AssignmentId = assignmentId,
            Reports = reports.Values
                .Where(r => r.AssignmentId == assignmentId)
                .Select(r => new ReportSummaryDto
                {
                    ReportId = r.ReportId,
                    WorkId = r.WorkId,
                    StudentId = r.StudentId,
                    IsPlagiarism = r.IsPlagiarism,
                    Status = r.Status,
                    CreatedAt = r.CreatedAt,
                    WordCloudUrl = r.WordCloudUrl
                })
                .OrderBy(r => r.CreatedAt)
                .ToList()
        };

        return Results.Ok(result);
    })
    .Produces<AssignmentReportsResponseDto>(StatusCodes.Status200OK)
    .WithName("GetReportsForAssignment")
    .WithTags("Reports");

app.MapGet("/reports/{reportId:int}", (
        int reportId) =>
    {
        if (!reports.TryGetValue(reportId, out var report))
        {
            return Results.Problem(
                title: "Отчёт не найден",
                detail: $"Отчёт с идентификатором {reportId} отсутствует",
                statusCode: StatusCodes.Status404NotFound);
        }

        return Results.Ok(report);
    })
    .Produces<ReportDetailsDto>(StatusCodes.Status200OK)
    .WithName("GetReportById")
    .WithTags("Reports");

app.Run();
