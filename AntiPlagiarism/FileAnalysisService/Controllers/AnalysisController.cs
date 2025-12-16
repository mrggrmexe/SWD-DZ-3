using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FileAnalysisService.Models;
using FileAnalysisService.Services;
using FileAnalysisService.Data;
using FileAnalysisService.Configuration;
using Shared.DTOs;
using System.Net.Http;
using System.Text.Json;

namespace FileAnalysisService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AnalysisController : ControllerBase
    {
        private readonly ILogger<AnalysisController> _logger;
        private readonly AppDbContext _context;
        private readonly IPlagiarismDetector _plagiarismDetector;
        private readonly IWordCloudService _wordCloudService;
        private readonly HttpClient _httpClient;
        private readonly ServiceSettings _settings;
        private readonly SemaphoreSlim _analysisSemaphore = new SemaphoreSlim(5, 10);

        public AnalysisController(
            ILogger<AnalysisController> logger,
            AppDbContext context,
            IPlagiarismDetector plagiarismDetector,
            IWordCloudService wordCloudService,
            IHttpClientFactory httpClientFactory,
            IOptions<ServiceSettings> settings)
        {
            _logger = logger;
            _context = context;
            _plagiarismDetector = plagiarismDetector;
            _wordCloudService = wordCloudService;
            _httpClient = httpClientFactory.CreateClient("FileStoringService");
            _settings = settings.Value;
        }

        [HttpPost("analyze/{workId}")]
        [ProducesResponseType(typeof(AnalysisResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponseDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponseDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponseDto), StatusCodes.Status429TooManyRequests)]
        [ProducesResponseType(typeof(ErrorResponseDto), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AnalyzeWork(int workId)
        {
            if (!await _analysisSemaphore.WaitAsync(TimeSpan.FromSeconds(1)))
            {
                _logger.LogWarning("Достигнут лимит параллельных анализов для работы {WorkId}", workId);
                return StatusCode(429, new ErrorResponseDto
                {
                    RequestId = HttpContext.TraceIdentifier,
                    Message = "Сервис перегружен. Попробуйте позже.",
                    Details = "Превышен лимит параллельных анализов (макс. 10)"
                });
            }

            try
            {
                _logger.LogInformation("Начинаем анализ работы {WorkId}", workId);

                var existingReport = await _context.Reports
                    .FirstOrDefaultAsync(r => r.WorkId == workId);

                if (existingReport != null)
                {
                    if (existingReport.Status == ReportStatus.Done)
                    {
                        _logger.LogInformation("Работа {WorkId} уже проанализирована", workId);
                        return Ok(new AnalysisResponseDto
                        {
                            ReportId = existingReport.Id,
                            WorkId = workId,
                            Status = "already_analyzed",
                            Message = "Работа уже была проанализирована ранее"
                        });
                    }
                    else if (existingReport.Status == ReportStatus.Pending)
                    {
                        _logger.LogWarning("Работа {WorkId} уже находится в процессе анализа", workId);
                        return BadRequest(new ErrorResponseDto
                        {
                            RequestId = HttpContext.TraceIdentifier,
                            Message = "Работа уже находится в процессе анализа",
                            Details = $"ReportId: {existingReport.Id}"
                        });
                    }
                }

                WorkMetaDto workMeta;
                try
                {
                    workMeta = await GetWorkMetadataWithRetryAsync(workId);
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogError(ex, "Не удалось получить метаданные работы {WorkId} после 3 попыток", workId);
                    
                    var errorReport = new Report
                    {
                        Id = new Random().Next(1, 1000000),
                        WorkId = workId,
                        Status = ReportStatus.Error,
                        CreatedAt = DateTime.UtcNow,
                        Details = $"Ошибка получения метаданных: {ex.Message}"
                    };
                    
                    await _context.Reports.AddAsync(errorReport);
                    await _context.SaveChangesAsync();
                    
                    return StatusCode(503, new ErrorResponseDto
                    {
                        RequestId = HttpContext.TraceIdentifier,
                        Message = "Сервис хранения файлов недоступен",
                        Details = $"WorkId: {workId}. Ошибка: {ex.Message}"
                    });
                }

                var report = new Report
                {
                    Id = new Random().Next(1, 1000000),
                    WorkId = workId,
                    StudentId = workMeta.StudentId,
                    AssignmentId = workMeta.AssignmentId,
                    Status = ReportStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                    Details = "Анализ запущен"
                };

                await _context.Reports.AddAsync(report);
                await _context.SaveChangesAsync();

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await PerformAnalysisAsync(report.Id, workId, workMeta);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Фоновая задача анализа завершилась с ошибкой для Report {ReportId}", report.Id);
                    }
                });

                return Accepted(new AnalysisResponseDto
                {
                    ReportId = report.Id,
                    WorkId = workId,
                    Status = "analysis_started",
                    Message = "Анализ запущен. Отчет будет готов через несколько секунд.",
                    EstimatedCompletion = DateTime.UtcNow.AddSeconds(10)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Неожиданная ошибка при запуске анализа работы {WorkId}", workId);
                return StatusCode(500, new ErrorResponseDto
                {
                    RequestId = HttpContext.TraceIdentifier,
                    Message = "Внутренняя ошибка сервера",
                    Details = $"WorkId: {workId}. Ошибка: {ex.Message}"
                });
            }
            finally
            {
                _analysisSemaphore.Release();
            }
        }

        [HttpGet("assignments/{assignmentId}/reports")]
        [ProducesResponseType(typeof(AssignmentReportsResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponseDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponseDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponseDto), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetAssignmentReports(int assignmentId)
        {
            try
            {
                _logger.LogInformation("Запрос отчетов для задания {AssignmentId}", assignmentId);

                var hasAssignment = await _context.Reports
                    .AnyAsync(r => r.AssignmentId == assignmentId);

                if (!hasAssignment)
                {
                    return NotFound(new ErrorResponseDto
                    {
                        RequestId = HttpContext.TraceIdentifier,
                        Message = "Отчеты по данному заданию не найдены",
                        Details = $"AssignmentId: {assignmentId}"
                    });
                }

                var reports = await _context.Reports
                    .Where(r => r.AssignmentId == assignmentId)
                    .OrderByDescending(r => r.CreatedAt)
                    .Select(r => new ReportSummaryDto
                    {
                        ReportId = r.Id,
                        WorkId = r.WorkId,
                        StudentId = r.StudentId,
                        IsPlagiarism = r.IsPlagiarism,
                        Status = r.Status.ToString().ToLower(),
                        CreatedAt = r.CreatedAt,
                        WordCloudUrl = r.WordCloudUrl
                    })
                    .ToListAsync();

                var response = new AssignmentReportsResponseDto
                {
                    AssignmentId = assignmentId,
                    TotalCount = reports.Count,
                    PlagiarismCount = reports.Count(r => r.IsPlagiarism),
                    Reports = reports.Take(100).ToList(),
                };

                _logger.LogInformation("Найдено {Count} отчетов для задания {AssignmentId}", reports.Count, assignmentId);
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении отчетов для задания {AssignmentId}", assignmentId);
                return StatusCode(500, new ErrorResponseDto
                {
                    RequestId = HttpContext.TraceIdentifier,
                    Message = "Ошибка при получении отчетов",
                    Details = $"AssignmentId: {assignmentId}. Ошибка: {ex.Message}"
                });
            }
        }

        [HttpGet("reports/{reportId}")]
        [ProducesResponseType(typeof(ReportDetailsDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponseDto), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetReportDetails(int reportId)
        {
            try
            {
                var report = await _context.Reports
                    .FirstOrDefaultAsync(r => r.Id == reportId);

                if (report == null)
                {
                    return NotFound(new ErrorResponseDto
                    {
                        RequestId = HttpContext.TraceIdentifier,
                        Message = "Отчет не найден",
                        Details = $"ReportId: {reportId}"
                    });
                }

                // Десериализуем источники плагиата
                var plagiarismSources = new List<PlagiarismSource>();
                if (!string.IsNullOrEmpty(report.PlagiarismSources))
                {
                    try
                    {
                        plagiarismSources = JsonSerializer.Deserialize<List<PlagiarismSource>>(
                            report.PlagiarismSources,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                        ) ?? new List<PlagiarismSource>();
                    }
                    catch (JsonException)
                    {
                        _logger.LogWarning("Не удалось десериализовать источники плагиата для отчета {ReportId}", reportId);
                    }
                }

                var response = new ReportDetailsDto
                {
                    ReportId = report.Id,
                    WorkId = report.WorkId,
                    StudentId = report.StudentId,
                    AssignmentId = report.AssignmentId,
                    IsPlagiarism = report.IsPlagiarism,
                    Status = report.Status.ToString().ToLower(),
                    CreatedAt = report.CreatedAt,
                    CompletedAt = report.CompletedAt,
                    WordCloudUrl = report.WordCloudUrl,
                    Details = report.Details,
                    SimilarityScore = plagiarismSources.Any() 
                        ? plagiarismSources.Average(s => s.SimilarityPercentage) / 100 
                        : (report.IsPlagiarism ? 0.8 : 0.1)
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении деталей отчета {ReportId}", reportId);
                return StatusCode(500, new ErrorResponseDto
                {
                    RequestId = HttpContext.TraceIdentifier,
                    Message = "Ошибка при получении деталей отчета",
                    Details = $"ReportId: {reportId}. Ошибка: {ex.Message}"
                });
            }
        }

        [HttpGet("status/{workId}")]
        public async Task<IActionResult> GetAnalysisStatus(int workId)
        {
            var report = await _context.Reports
                .Where(r => r.WorkId == workId)
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefaultAsync();

            if (report == null)
            {
                return NotFound(new { 
                    WorkId = workId, 
                    Status = "not_found",
                    Message = "Анализ для данной работы не запускался"
                });
            }

            return Ok(new
            {
                ReportId = report.Id,
                WorkId = report.WorkId,
                Status = report.Status.ToString().ToLower(),
                report.CreatedAt,
                report.CompletedAt,
                report.Details
            });
        }

        #region Вспомогательные методы

        private async Task<WorkMetaDto> GetWorkMetadataWithRetryAsync(int workId, int maxRetries = 3)
        {
            var retryCount = 0;
            var baseDelay = TimeSpan.FromSeconds(1);

            while (retryCount < maxRetries)
            {
                try
                {
                    var response = await _httpClient.GetAsync($"api/files/{workId}/meta");
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        var meta = JsonSerializer.Deserialize<WorkMetaDto>(json, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        if (meta != null)
                        {
                            _logger.LogDebug("Получены метаданные для работы {WorkId}", workId);
                            return meta;
                        }
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        throw new InvalidOperationException($"Работа с ID {workId} не найдена в хранилище");
                    }
                }
                catch (HttpRequestException ex) when (retryCount < maxRetries - 1)
                {
                    retryCount++;
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, retryCount));
                    _logger.LogWarning(ex, "Попытка {RetryCount} получения метаданных не удалась. Повтор через {Delay}с", 
                        retryCount, delay.TotalSeconds);
                    await Task.Delay(delay);
                    continue;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Критическая ошибка при получении метаданных работы {WorkId}", workId);
                    throw;
                }

                retryCount++;
                if (retryCount < maxRetries)
                {
                    await Task.Delay(baseDelay * retryCount);
                }
            }

            throw new HttpRequestException($"Не удалось получить метаданные работы {workId} после {maxRetries} попыток");
        }

        private async Task PerformAnalysisAsync(int reportId, int workId, WorkMetaDto workMeta)
        {
            var report = await _context.Reports.FindAsync(reportId);
            if (report == null) return;

            try
            {
                _logger.LogInformation("Начинаем детальный анализ для отчета {ReportId}", reportId);

                string fileContent = null;
                try
                {
                    var fileResponse = await _httpClient.GetAsync($"api/files/{workId}/download");
                    if (fileResponse.IsSuccessStatusCode)
                    {
                        fileContent = await fileResponse.Content.ReadAsStringAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Не удалось получить содержимое файла для работы {WorkId}", workId);
                }

                var plagiarismResult = await _plagiarismDetector.CheckForPlagiarismAsync(workMeta, _context);
                report.IsPlagiarism = plagiarismResult.IsPlagiarism;
                
                // СЕРИАЛИЗУЕМ источники плагиата в JSON
                if (plagiarismResult.Sources != null && plagiarismResult.Sources.Any())
                {
                    report.PlagiarismSources = JsonSerializer.Serialize(
                        plagiarismResult.Sources,
                        new JsonSerializerOptions { WriteIndented = false }
                    );
                }
                else
                {
                    report.PlagiarismSources = null;
                }
                
                report.Details = plagiarismResult.Details;

                if (!string.IsNullOrEmpty(fileContent))
                {
                    try
                    {
                        report.WordCloudUrl = await _wordCloudService.GenerateWordCloudAsync(fileContent);
                        _logger.LogInformation("Word Cloud сгенерирован для отчета {ReportId}", reportId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Не удалось сгенерировать Word Cloud для отчета {ReportId}", reportId);
                        report.Details += $"\nОшибка генерации Word Cloud: {ex.Message}";
                    }
                }

                report.Status = ReportStatus.Done;
                report.CompletedAt = DateTime.UtcNow;
                
                if (string.IsNullOrEmpty(report.Details))
                {
                    report.Details = $"Анализ завершен. Плагиат: {(report.IsPlagiarism ? "обнаружен" : "не обнаружен")}";
                }

                await _context.SaveChangesAsync();
                _logger.LogInformation("Анализ успешно завершен для отчета {ReportId}", reportId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении анализа для отчета {ReportId}", reportId);
                
                report.Status = ReportStatus.Error;
                report.Details = $"Ошибка анализа: {ex.Message}";
                report.CompletedAt = DateTime.UtcNow;
                
                await _context.SaveChangesAsync();
            }
        }

        #endregion
    }
}