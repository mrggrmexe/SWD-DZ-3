using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Shared.DTOs;
using System.Text.Json;

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
    client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
    client.Timeout = TimeSpan.FromSeconds(10);
});

// Кэш для метаданных работ (workId -> метаданные)
var works = new ConcurrentDictionary<int, WorkMetaDto>();

// Кэш для отчётов (reportId -> отчёт)
var reports = new ConcurrentDictionary<int, ReportDetailsDto>();
var reportIdCounter = 0;

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/health", () => Results.Ok(new
{
    status = "FileAnalysisService OK",
    timestamp = DateTime.UtcNow,
    service = "FileAnalysisService",
    version = "1.0.0",
    metrics = new
    {
        cachedWorks = works.Count,
        cachedReports = reports.Count
    }
}))
.WithName("FileAnalysisHealth")
.WithTags("Health");

app.MapPost("/analyze/{workId}", async (
        string workId,
        HttpContext context,
        [FromServices] IHttpClientFactory httpClientFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        
        // Получаем correlationId из контекста
        var correlationId = context.Request.Headers["X-Correlation-Id"].FirstOrDefault() 
            ?? Guid.NewGuid().ToString();
        
        logger.LogInformation("[{CorrelationId}] Starting analysis for workId={WorkId}", 
            correlationId, workId);

        // Проверяем формат workId
        if (string.IsNullOrWhiteSpace(workId))
        {
            logger.LogWarning("[{CorrelationId}] Empty workId provided", correlationId);
            return Results.Problem(
                title: "Некорректный идентификатор работы",
                detail: "workId не может быть пустым",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Преобразуем workId в int для кэша
        if (!int.TryParse(workId, out int workIdInt))
        {
            logger.LogWarning("[{CorrelationId}] Invalid workId format: {WorkId}", correlationId, workId);
            return Results.Problem(
                title: "Неверный формат workId",
                detail: "workId должен быть числом",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // 1. Получаем метаданные работы из FileStoringService
        WorkMetaDto workMeta;
        
        if (works.TryGetValue(workIdInt, out var cachedWorkMeta))
        {
            logger.LogDebug("[{CorrelationId}] Found cached metadata for workId={WorkId}", 
                correlationId, workId);
            workMeta = cachedWorkMeta;
        }
        else
        {
            logger.LogDebug("[{CorrelationId}] Fetching metadata from FileStoringService for workId={WorkId}", 
                correlationId, workId);
            
            var fileStoreClient = httpClientFactory.CreateClient("FileStoringService");

            // Добавляем correlationId в запрос
            var request = new HttpRequestMessage(HttpMethod.Get, $"/api/files/{workIdInt}/meta");
            request.Headers.Add("X-Correlation-Id", correlationId);

            HttpResponseMessage response;
            try
            {
                response = await fileStoreClient.SendAsync(request, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[{CorrelationId}] Failed to connect to FileStoringService for workId={WorkId}", 
                    correlationId, workId);
                
                return Results.Problem(
                    title: "Сервис хранения недоступен",
                    detail: "Не удалось получить метаданные работы. Попробуйте позже.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                logger.LogWarning("[{CorrelationId}] Work not found: workId={WorkId}", 
                    correlationId, workId);
                
                return Results.Problem(
                    title: "Сдача не найдена",
                    detail: $"Сдача с идентификатором {workId} отсутствует в FileStoringService",
                    statusCode: StatusCodes.Status404NotFound);
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning("[{CorrelationId}] FileStoringService returned error for workId={WorkId}: {Status} {Body}", 
                    correlationId, workId, response.StatusCode, body);

                return Results.Problem(
                    title: "Ошибка FileStoringService",
                    detail: $"Не удалось получить метаданные сдачи (статус {(int)response.StatusCode})",
                    statusCode: StatusCodes.Status502BadGateway);
            }

            // Парсим JSON ответ
            try
            {
                workMeta = await response.Content.ReadFromJsonAsync<WorkMetaDto>(cancellationToken);
                if (workMeta == null)
                {
                    logger.LogError("[{CorrelationId}] Failed to parse metadata for workId={WorkId}", correlationId, workId);
                    return Results.Problem(
                        title: "Ошибка обработки данных",
                        detail: "Не удалось обработать метаданные сдачи",
                        statusCode: StatusCodes.Status500InternalServerError);
                }

                // Кэшируем результат
                works[workIdInt] = workMeta;
                
                logger.LogInformation("[{CorrelationId}] Retrieved metadata for workId={WorkId}: studentId={StudentId}, assignmentId={AssignmentId}", 
                    correlationId, workId, workMeta.StudentId, workMeta.AssignmentId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[{CorrelationId}] Failed to parse metadata from FileStoringService for workId={WorkId}", 
                    correlationId, workId);
                
                return Results.Problem(
                    title: "Ошибка обработки данных",
                    detail: "Не удалось обработать метаданные сдачи",
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        }

        // 2. Реализуем простой алгоритм плагиата:
        // есть ли более ранняя сдача этой же работы (assignmentId), но от другого студента?
        logger.LogDebug("[{CorrelationId}] Checking for plagiarism: assignmentId={AssignmentId}, studentId={StudentId}, submittedAt={SubmittedAt}", 
            correlationId, workMeta.AssignmentId, workMeta.StudentId, workMeta.SubmittedAt);

        var isPlagiarism = works.Values.Any(x =>
            x.AssignmentId == workMeta.AssignmentId &&
            x.StudentId != workMeta.StudentId &&
            x.SubmittedAt < workMeta.SubmittedAt);

        var reportId = Interlocked.Increment(ref reportIdCounter);
        var createdAt = DateTime.UtcNow;

        // Создаем отчет
        var report = new ReportDetailsDto
        {
            ReportId = reportId,
            WorkId = workMeta.WorkId,
            AssignmentId = workMeta.AssignmentId,
            StudentId = workMeta.StudentId,
            IsPlagiarism = isPlagiarism,
            Status = "Done",
            CreatedAt = createdAt,
            CompletedAt = createdAt,
            Details = isPlagiarism
                ? "Обнаружена более ранняя сдача той же работы другим студентом"
                : "Более ранних сдач этой работы другими студентами не обнаружено",
            WordCloudUrl = null,
            SimilarityScore = isPlagiarism ? 1.0 : 0.0
        };

        reports[reportId] = report;

        logger.LogInformation("[{CorrelationId}] Analysis completed for workId={WorkId}: plagiarism={Plagiarism}, reportId={ReportId}", 
            correlationId, workId, isPlagiarism, reportId);

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

        // Добавляем заголовки для трассировки
        context.Response.Headers.Append("X-Correlation-Id", correlationId);
        context.Response.Headers.Append("X-Report-Id", reportId.ToString());

        return Results.Ok(summary);
    })
    .WithTags("Analysis")
    .WithName("AnalyzeWork")
    .Accepts<string>("application/json")
    .Produces<ReportSummaryDto>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status502BadGateway)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapGet("/analyze/assignment/{assignmentId}/reports", (
        string assignmentId,
        HttpContext context) =>
    {
        if (string.IsNullOrWhiteSpace(assignmentId))
        {
            return Results.Problem(
                title: "Некорректный идентификатор задания",
                detail: "assignmentId не может быть пустым",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Проверяем, является ли assignmentId числом
        if (!int.TryParse(assignmentId, out int assignmentIdInt))
        {
            return Results.Problem(
                title: "Неверный формат assignmentId",
                detail: "assignmentId должен быть числом",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var relevantReports = reports.Values
            .Where(r => r.AssignmentId == assignmentIdInt)
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
            .ToList();

        var result = new AssignmentReportsResponseDto
        {
            AssignmentId = assignmentIdInt,
            Reports = relevantReports,
            TotalCount = relevantReports.Count,
            PlagiarismCount = relevantReports.Count(r => r.IsPlagiarism)
        };

        return Results.Ok(result);
    })
    .WithTags("Reports")
    .WithName("GetReportsForAssignment")
    .Produces<AssignmentReportsResponseDto>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status400BadRequest);

app.MapGet("/analyze/reports/{reportId}", (
        string reportId,
        HttpContext context) =>
    {
        if (string.IsNullOrWhiteSpace(reportId))
        {
            return Results.Problem(
                title: "Некорректный идентификатор отчёта",
                detail: "reportId не может быть пустым",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!int.TryParse(reportId, out int reportIdInt))
        {
            return Results.Problem(
                title: "Неверный формат reportId",
                detail: "reportId должен быть числом",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!reports.TryGetValue(reportIdInt, out var report))
        {
            return Results.Problem(
                title: "Отчёт не найден",
                detail: $"Отчёт с идентификатором {reportId} отсутствует",
                statusCode: StatusCodes.Status404NotFound);
        }

        return Results.Ok(report);
    })
    .WithTags("Reports")
    .WithName("GetReportById")
    .Produces<ReportDetailsDto>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status404NotFound);

app.Run();