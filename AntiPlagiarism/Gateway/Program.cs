using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.OpenApi;
using Shared.DTOs;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// ProblemDetails для единообразных ошибок
builder.Services.AddProblemDetails();

// Configure HTTP clients
builder.Services.AddHttpClient("FileStoringService", (sp, client) =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var baseUrl = configuration["FILE_STORING_BASE_URL"] ?? "http://localhost:5001";
    client.BaseAddress = new Uri(baseUrl);
    client.DefaultRequestHeaders.Accept.Add(
        new MediaTypeWithQualityHeaderValue("application/json"));
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddHttpClient("FileAnalysisService", (sp, client) =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var baseUrl = configuration["FILE_ANALYSIS_BASE_URL"] ?? "http://localhost:5002";
    client.BaseAddress = new Uri(baseUrl);
    client.DefaultRequestHeaders.Accept.Add(
        new MediaTypeWithQualityHeaderValue("application/json"));
    client.Timeout = TimeSpan.FromSeconds(10);
});

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure middleware pipeline
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseHttpsRedirection();

// CORS
if (app.Environment.IsDevelopment())
{
    app.UseCors("AllowAll");
}

// Correlation ID middleware
app.Use(async (context, next) =>
{
    const string headerName = "X-Correlation-Id";

    var correlationId = context.Request.Headers.TryGetValue(headerName, out var value) &&
                        !string.IsNullOrWhiteSpace(value)
        ? value.ToString()
        : Guid.NewGuid().ToString();

    context.Items[headerName] = correlationId;
    context.Response.Headers[headerName] = correlationId;

    var logger = context.RequestServices
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("CorrelationId");

    using (logger.BeginScope(new Dictionary<string, object>
           {
               [headerName] = correlationId
           }))
    {
        await next();
    }
});

// Health endpoint
app.MapGet("/health", () => 
    Results.Ok(new
    {
        status = "Gateway OK",
        timestamp = DateTime.UtcNow,
        service = "Gateway",
        version = "1.0.0"
    }))
    .WithTags("Health")
    .WithName("GatewayHealth");

// Upload work endpoint
app.MapPost("/works/upload", async (
        HttpContext context,
        [FromServices] IHttpClientFactory httpClientFactory,
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
        var studentIdValue = form["studentId"].ToString();
        var assignmentIdValue = form["assignmentId"].ToString();

        if (file is null || file.Length == 0)
        {
            return Results.Problem(
                title: "Файл не передан",
                detail: "Поле 'file' обязательно и не может быть пустым",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(studentIdValue) ||
            string.IsNullOrWhiteSpace(assignmentIdValue))
        {
            return Results.Problem(
                title: "Неверные данные",
                detail: "Поля 'studentId' и 'assignmentId' обязательны",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Parse IDs
        if (!int.TryParse(studentIdValue, out int studentId))
        {
            return Results.Problem(
                title: "Неверный формат studentId",
                detail: "studentId должен быть числом",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!int.TryParse(assignmentIdValue, out int assignmentId))
        {
            return Results.Problem(
                title: "Неверный формат assignmentId",
                detail: "assignmentId должен быть числом",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var correlationId = (string)context.Items["X-Correlation-Id"]!;

        var fileStoringClient = httpClientFactory.CreateClient("FileStoringService");

        using var multipartContent = new MultipartFormDataContent();

        var fileContent = new StreamContent(file.OpenReadStream());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
        multipartContent.Add(fileContent, "file", file.FileName);

        multipartContent.Add(new StringContent(studentId.ToString()), "studentId");
        multipartContent.Add(new StringContent(assignmentId.ToString()), "assignmentId");

        using var fileStoreRequest = new HttpRequestMessage(HttpMethod.Post, "/api/files")
        {
            Content = multipartContent
        };
        fileStoreRequest.Headers.Add("X-Correlation-Id", correlationId);

        HttpResponseMessage fileStoreResponse;
        try
        {
            fileStoreResponse = await fileStoringClient.SendAsync(
                fileStoreRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Ошибка при обращении к FileStoringService при загрузке работы");

            return Results.Problem(
                title: "Сервис хранения файлов недоступен",
                detail: "Не удалось сохранить работу. Попробуйте позже.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (!fileStoreResponse.IsSuccessStatusCode)
        {
            var body = await fileStoreResponse.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning("FileStoringService вернул ошибку: {Status} {Body}",
                fileStoreResponse.StatusCode, body);

            return Results.Problem(
                title: "Не удалось сохранить работу",
                detail: $"Сервис хранения вернул статус {(int)fileStoreResponse.StatusCode}",
                statusCode: StatusCodes.Status502BadGateway);
        }

        // Используем JsonDocument для парсинга ответа
        JsonDocument? jsonDoc;
        try
        {
            var jsonString = await fileStoreResponse.Content.ReadAsStringAsync(cancellationToken);
            jsonDoc = JsonDocument.Parse(jsonString);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Не удалось распарсить ответ FileStoringService");

            return Results.Problem(
                title: "Ошибка сервиса хранения",
                detail: "Получен некорректный ответ при сохранении работы",
                statusCode: StatusCodes.Status502BadGateway);
        }

        var root = jsonDoc.RootElement;
        
        // Извлекаем значения из JSON
        int parsedWorkId = 0;
        int parsedAssignmentId = 0;
        int parsedStudentId = 0;
        DateTime parsedSubmittedAt = DateTime.UtcNow;

        // Пробуем разные варианты имен свойств
        if (root.TryGetProperty("workId", out var workIdElement) && workIdElement.ValueKind == JsonValueKind.Number)
        {
            parsedWorkId = workIdElement.GetInt32();
        }
        else if (root.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.Number)
        {
            parsedWorkId = idElement.GetInt32();
        }
        else if (root.TryGetProperty("WorkId", out var workIdElementUpper) && workIdElementUpper.ValueKind == JsonValueKind.Number)
        {
            parsedWorkId = workIdElementUpper.GetInt32();
        }

        if (root.TryGetProperty("studentId", out var studentIdElement) && studentIdElement.ValueKind == JsonValueKind.Number)
        {
            parsedStudentId = studentIdElement.GetInt32();
        }
        else if (root.TryGetProperty("StudentId", out var studentIdElementUpper) && studentIdElementUpper.ValueKind == JsonValueKind.Number)
        {
            parsedStudentId = studentIdElementUpper.GetInt32();
        }

        if (root.TryGetProperty("assignmentId", out var assignmentIdElement) && assignmentIdElement.ValueKind == JsonValueKind.Number)
        {
            parsedAssignmentId = assignmentIdElement.GetInt32();
        }
        else if (root.TryGetProperty("AssignmentId", out var assignmentIdElementUpper) && assignmentIdElementUpper.ValueKind == JsonValueKind.Number)
        {
            parsedAssignmentId = assignmentIdElementUpper.GetInt32();
        }

        if (root.TryGetProperty("submittedAt", out var submittedAtElement))
        {
            if (submittedAtElement.ValueKind == JsonValueKind.String &&
                DateTime.TryParse(submittedAtElement.GetString(), out var dateTime))
            {
                parsedSubmittedAt = dateTime;
            }
            else if (submittedAtElement.ValueKind == JsonValueKind.Number)
            {
                // Если timestamp в миллисекундах
                var timestamp = submittedAtElement.GetInt64();
                parsedSubmittedAt = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).UtcDateTime;
            }
        }

        // Используем строковый workId для вызова анализа
        string workIdForAnalysis = parsedWorkId.ToString();

        var analysisClient = httpClientFactory.CreateClient("FileAnalysisService");
        
        var uploadResponse = new UploadWorkResponseDto
        {
            WorkId = parsedWorkId,
            AssignmentId = parsedAssignmentId,
            StudentId = parsedStudentId,
            SubmittedAt = parsedSubmittedAt,
            AnalysisStarted = false,
            CorrelationId = correlationId
        };

        try
        {
            using var analysisRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/analyze/{workIdForAnalysis}");

            analysisRequest.Headers.Add("X-Correlation-Id", correlationId);

            var analysisResponse = await analysisClient.SendAsync(
                analysisRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (analysisResponse.IsSuccessStatusCode)
            {
                uploadResponse.AnalysisStarted = true;
            }
            else
            {
                var body = await analysisResponse.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning(
                    "FileAnalysisService вернул ошибку при анализе workId={WorkId}: {Status} {Body}",
                    workIdForAnalysis, analysisResponse.StatusCode, body);

                uploadResponse.Error =
                    $"Анализ не был запущен. Статус: {(int)analysisResponse.StatusCode}";
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Ошибка при обращении к FileAnalysisService для workId={WorkId}",
                workIdForAnalysis);

            uploadResponse.Error =
                "Работа сохранена, но сервис анализа временно недоступен. " +
                "Вы можете запросить отчёты позднее.";
        }

        // Получаем download URL из ответа FileStoringService
        if (root.TryGetProperty("downloadUrl", out var downloadUrlElement) && 
            downloadUrlElement.ValueKind == JsonValueKind.String)
        {
            uploadResponse.FileUrl = downloadUrlElement.GetString();
        }
        else if (root.TryGetProperty("fileUrl", out var fileUrlElement) && 
                 fileUrlElement.ValueKind == JsonValueKind.String)
        {
            uploadResponse.FileUrl = fileUrlElement.GetString();
        }
        else
        {
            // Собираем URL сами
            uploadResponse.FileUrl = $"/api/works/{workIdForAnalysis}/download";
        }

        return Results.Ok(uploadResponse);
    })
    .WithTags("Works")
    .WithName("UploadWork")
    .Accepts<IFormFile>("multipart/form-data")
    .Produces<UploadWorkResponseDto>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status502BadGateway)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

// Get assignment reports endpoint
app.MapGet("/assignments/{assignmentId}/reports", async (
        string assignmentId,
        HttpContext context,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromServices] ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        if (string.IsNullOrWhiteSpace(assignmentId))
        {
            return Results.Problem(
                title: "Некорректный идентификатор задания",
                detail: "assignmentId не может быть пустым",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Validate assignmentId is a number
        if (!int.TryParse(assignmentId, out int assignmentIdInt))
        {
            return Results.Problem(
                title: "Неверный формат assignmentId",
                detail: "assignmentId должен быть числом",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var correlationId = (string)context.Items["X-Correlation-Id"]!;
        var analysisClient = httpClientFactory.CreateClient("FileAnalysisService");

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/analyze/assignment/{assignmentIdInt}/reports");

        request.Headers.Add("X-Correlation-Id", correlationId);

        HttpResponseMessage response;
        try
        {
            response = await analysisClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Ошибка при обращении к FileAnalysisService для assignmentId={AssignmentId}",
                assignmentId);

            return Results.Problem(
                title: "Сервис анализа недоступен",
                detail: "Не удалось получить отчёты. Попробуйте позже.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Results.Problem(
                title: "Задание не найдено",
                detail: $"Отчёты по заданию '{assignmentId}' отсутствуют",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning(
                "FileAnalysisService вернул ошибку для assignmentId={AssignmentId}: {Status} {Body}",
                assignmentId, response.StatusCode, body);

            return Results.Problem(
                title: "Ошибка при получении отчётов",
                detail: $"Сервис анализа вернул статус {(int)response.StatusCode}",
                statusCode: StatusCodes.Status502BadGateway);
        }

        AssignmentReportsResponseDto? result;
        try
        {
            result = await response.Content
                .ReadFromJsonAsync<AssignmentReportsResponseDto>(cancellationToken: cancellationToken);

            if (result is null)
                throw new InvalidOperationException("Пустой ответ от FileAnalysisService");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Не удалось распарсить ответ FileAnalysisService как AssignmentReportsResponseDto");

            return Results.Problem(
                title: "Ошибка сервиса анализа",
                detail: "Получен некорректный ответ при получении отчётов",
                statusCode: StatusCodes.Status502BadGateway);
        }

        return Results.Ok(result);
    })
    .WithTags("Reports")
    .WithName("GetAssignmentReports")
    .Produces<AssignmentReportsResponseDto>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status502BadGateway)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

// Get report by ID endpoint
app.MapGet("/reports/{reportId}", async (
        string reportId,
        HttpContext context,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromServices] ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        if (string.IsNullOrWhiteSpace(reportId))
        {
            return Results.Problem(
                title: "Некорректный идентификатор отчёта",
                detail: "reportId не может быть пустым",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Validate reportId is a number
        if (!int.TryParse(reportId, out int reportIdInt))
        {
            return Results.Problem(
                title: "Неверный формат reportId",
                detail: "reportId должен быть числом",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var correlationId = (string)context.Items["X-Correlation-Id"]!;
        var analysisClient = httpClientFactory.CreateClient("FileAnalysisService");

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/analyze/reports/{reportIdInt}");

        request.Headers.Add("X-Correlation-Id", correlationId);

        HttpResponseMessage response;
        try
        {
            response = await analysisClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Ошибка при обращении к FileAnalysisService для reportId={ReportId}",
                reportId);

            return Results.Problem(
                title: "Сервис анализа недоступен",
                detail: "Не удалось получить отчёт. Попробуйте позже.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Results.Problem(
                title: "Отчёт не найден",
                detail: $"Отчёт с идентификатором {reportId} отсутствует",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning(
                "FileAnalysisService вернул ошибку для reportId={ReportId}: {Status} {Body}",
                reportId, response.StatusCode, body);

            return Results.Problem(
                title: "Ошибка при получении отчёта",
                detail: $"Сервис анализа вернул статус {(int)response.StatusCode}",
                statusCode: StatusCodes.Status502BadGateway);
        }

        ReportDetailsDto? result;
        try
        {
            result = await response.Content
                .ReadFromJsonAsync<ReportDetailsDto>(cancellationToken: cancellationToken);

            if (result is null)
                throw new InvalidOperationException("Пустой ответ от FileAnalysisService");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Не удалось распарсить ответ FileAnalysisService как ReportDetailsDto");

            return Results.Problem(
                title: "Ошибка сервиса анализа",
                detail: "Получен некорректный ответ при получении отчёта",
                statusCode: StatusCodes.Status502BadGateway);
        }

        return Results.Ok(result);
    })
    .WithTags("Reports")
    .WithName("GetReportById")
    .Produces<ReportDetailsDto>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status502BadGateway)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

// Download work endpoint
app.MapGet("/works/{workId}/download", async (
        string workId,
        HttpContext context,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromServices] ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        if (string.IsNullOrWhiteSpace(workId))
        {
            return Results.Problem(
                title: "Некорректный идентификатор работы",
                detail: "workId не может быть пустым",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var correlationId = (string)context.Items["X-Correlation-Id"]!;
        var fileStoringClient = httpClientFactory.CreateClient("FileStoringService");

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/files/{workId}/download");

        request.Headers.Add("X-Correlation-Id", correlationId);

        HttpResponseMessage response;
        try
        {
            response = await fileStoringClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Ошибка при обращении к FileStoringService для workId={WorkId}",
                workId);

            return Results.Problem(
                title: "Сервис хранения недоступен",
                detail: "Не удалось загрузить файл. Попробуйте позже.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Results.Problem(
                title: "Работа не найдена",
                detail: $"Работа с идентификатором {workId} отсутствует",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning(
                "FileStoringService вернул ошибку для workId={WorkId}: {Status} {Body}",
                workId, response.StatusCode, body);

            return Results.Problem(
                title: "Ошибка при загрузке файла",
                detail: $"Сервис хранения вернул статус {(int)response.StatusCode}",
                statusCode: StatusCodes.Status502BadGateway);
        }

        // Stream the file response
        var fileStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
        var fileName = response.Content.Headers.ContentDisposition?.FileName ?? "work.bin";

        return Results.File(fileStream, contentType, fileName);
    })
    .WithTags("Works")
    .WithName("DownloadWork")
    .Produces<FileContentResult>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status502BadGateway)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

// Work metadata endpoint
app.MapGet("/works/{workId}/meta", async (
        string workId,
        HttpContext context,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromServices] ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        if (string.IsNullOrWhiteSpace(workId))
        {
            return Results.Problem(
                title: "Некорректный идентификатор работы",
                detail: "workId не может быть пустым",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var correlationId = (string)context.Items["X-Correlation-Id"]!;
        var fileStoringClient = httpClientFactory.CreateClient("FileStoringService");

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/files/{workId}/meta");

        request.Headers.Add("X-Correlation-Id", correlationId);

        HttpResponseMessage response;
        try
        {
            response = await fileStoringClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Ошибка при обращении к FileStoringService для workId={WorkId}",
                workId);

            return Results.Problem(
                title: "Сервис хранения недоступен",
                detail: "Не удалось получить метаданные. Попробуйте позже.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Results.Problem(
                title: "Работа не найдена",
                detail: $"Работа с идентификатором {workId} отсутствует",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning(
                "FileStoringService вернул ошибку для workId={WorkId}: {Status} {Body}",
                workId, response.StatusCode, body);

            return Results.Problem(
                title: "Ошибка при получении метаданных",
                detail: $"Сервис хранения вернул статус {(int)response.StatusCode}",
                statusCode: StatusCodes.Status502BadGateway);
        }

        try
        {
            var jsonString = await response.Content.ReadAsStringAsync(cancellationToken);
            return Results.Text(jsonString, "application/json");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Не удалось прочитать ответ FileStoringService для workId={WorkId}",
                workId);

            return Results.Problem(
                title: "Ошибка при обработке метаданных",
                detail: "Не удалось обработать ответ от сервиса хранения",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    })
    .WithTags("Works")
    .WithName("GetWorkMeta")
    .Produces<string>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status502BadGateway)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

// Simple status endpoint
app.MapGet("/", () => Results.Ok(new
{
    service = "AntiPlagiarism Gateway",
    version = "1.0.0",
    status = "running",
    timestamp = DateTime.UtcNow,
    endpoints = new[]
    {
        new { path = "/health", method = "GET", description = "Health check" },
        new { path = "/works/upload", method = "POST", description = "Upload work" },
        new { path = "/works/{workId}/download", method = "GET", description = "Download work" },
        new { path = "/works/{workId}/meta", method = "GET", description = "Get work metadata" },
        new { path = "/assignments/{assignmentId}/reports", method = "GET", description = "Get assignment reports" },
        new { path = "/reports/{reportId}", method = "GET", description = "Get report details" }
    }
}))
.WithTags("Status")
.WithName("Root");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseDeveloperExceptionPage();
}

app.Run();