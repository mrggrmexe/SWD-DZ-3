using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Shared.DTOs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();

// ProblemDetails для единообразных ошибок
builder.Services.AddProblemDetails();

builder.Services.AddHttpClient("FileStoringService", (sp, client) =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var baseUrl = configuration["FILE_STORING_BASE_URL"] ?? "http://localhost:5001";
    client.BaseAddress = new Uri(baseUrl);
    client.DefaultRequestHeaders.Accept.Add(
        new MediaTypeWithQualityHeaderValue("application/json"));

    // Простой таймаут на всякий случай
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

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseHttpsRedirection();

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

app.MapGet("/health", () => Results.Ok("Gateway OK"))
    .WithName("GatewayHealth")
    .WithTags("Health");

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

        var correlationId = (string)context.Items["X-Correlation-Id"]!;

        var fileStoringClient = httpClientFactory.CreateClient("FileStoringService");

        using var multipartContent = new MultipartFormDataContent();

        var fileContent = new StreamContent(file.OpenReadStream());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
        multipartContent.Add(fileContent, "file", file.FileName);

        multipartContent.Add(new StringContent(studentId), "studentId");
        multipartContent.Add(new StringContent(assignmentId), "assignmentId");

        using var fileStoreRequest = new HttpRequestMessage(HttpMethod.Post, "/files")
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

        WorkMetaDto? workMeta;
        try
        {
            workMeta = await fileStoreResponse.Content
                .ReadFromJsonAsync<WorkMetaDto>(cancellationToken: cancellationToken);

            if (workMeta is null)
                throw new InvalidOperationException("Пустой ответ от FileStoringService");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Не удалось распарсить ответ FileStoringService как WorkMetaDto");

            return Results.Problem(
                title: "Ошибка сервиса хранения",
                detail: "Получен некорректный ответ при сохранении работы",
                statusCode: StatusCodes.Status502BadGateway);
        }

        var analysisClient = httpClientFactory.CreateClient("FileAnalysisService");
        var uploadResponse = new UploadWorkResponseDto
        {
            WorkId = workMeta.WorkId,
            AssignmentId = workMeta.AssignmentId,
            StudentId = workMeta.StudentId,
            SubmittedAt = workMeta.SubmittedAt,
            AnalysisStarted = false
        };

        try
        {
            using var analysisRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"/analyze/{workMeta.WorkId}");

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
                    workMeta.WorkId, analysisResponse.StatusCode, body);

                uploadResponse.Error =
                    $"Анализ не был запущен. Статус: {(int)analysisResponse.StatusCode}";
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Ошибка при обращении к FileAnalysisService для workId={WorkId}",
                workMeta.WorkId);

            uploadResponse.Error =
                "Работа сохранена, но сервис анализа временно недоступен. " +
                "Вы можете запросить отчёты позднее.";
        }

        return Results.Ok(uploadResponse);
    })
    .WithName("UploadWork")
    .WithTags("Works");

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

        var correlationId = (string)context.Items["X-Correlation-Id"]!;
        var analysisClient = httpClientFactory.CreateClient("FileAnalysisService");

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/assignments/{assignmentId}/reports");

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
    .WithName("GetAssignmentReports")
    .WithTags("Reports");

app.MapGet("/reports/{reportId:int}", async (
        int reportId,
        HttpContext context,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromServices] ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        if (reportId <= 0)
        {
            return Results.Problem(
                title: "Некорректный идентификатор отчёта",
                detail: "reportId должен быть положительным",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var correlationId = (string)context.Items["X-Correlation-Id"]!;
        var analysisClient = httpClientFactory.CreateClient("FileAnalysisService");

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/reports/{reportId}");

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
    .WithName("GetReportById")
    .WithTags("Reports");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.Run();
