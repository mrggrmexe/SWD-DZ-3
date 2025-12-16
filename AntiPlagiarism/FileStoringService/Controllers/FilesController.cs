using Microsoft.AspNetCore.Mvc;
using Shared.DTOs;
using FileStoringService.Repositories;
using FileStoringService.Models;
using FileStoringService.Helpers;
using System.Text.Json;

namespace FileStoringService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FilesController : ControllerBase
{
    private readonly IWorkRepository _repository;
    private readonly ILogger<FilesController> _logger;
    private readonly string _storagePath;
    private readonly JsonSerializerOptions _jsonOptions;

    public FilesController(
        IWorkRepository repository, 
        ILogger<FilesController> logger, 
        IConfiguration configuration)
    {
        _repository = repository;
        _logger = logger;
        _storagePath = configuration["Storage:Path"] ?? "storage";
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
        
        EnsureStorageDirectory();
    }

    private void EnsureStorageDirectory()
    {
        try
        {
            if (!Directory.Exists(_storagePath))
            {
                Directory.CreateDirectory(_storagePath);
                _logger.LogInformation("Created storage directory: {StoragePath}", _storagePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create storage directory: {StoragePath}", _storagePath);
            throw new InvalidOperationException($"Cannot create storage directory: {_storagePath}", ex);
        }
    }

    [HttpPost]
    public async Task<ActionResult<WorkResponseDto>> UploadFile(
        [FromForm] int studentId, 
        [FromForm] int assignmentId, 
        [FromForm] IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("File is required");

        // Генерируем уникальный ID
        var workId = Guid.NewGuid().ToString();
        var fileName = $"{workId}_{file.FileName}";
        var filePath = Path.Combine(_storagePath, fileName);

        // Сохраняем файл
        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        // Сохраняем метаданные
        var work = new Work
        {
            WorkId = workId,
            StudentId = studentId,
            AssignmentId = assignmentId,
            SubmittedAt = DateTime.UtcNow,
            FileName = file.FileName,
            FilePath = filePath,
            FileSize = file.Length
        };

        _repository.Add(work);

        var response = new WorkResponseDto
        {
            WorkId = int.Parse(workId.Replace("-", "").Substring(0, 8), System.Globalization.NumberStyles.HexNumber),
            StudentId = work.StudentId,
            AssignmentId = work.AssignmentId,
            SubmittedAt = work.SubmittedAt,
            FilePath = work.FilePath,
            FileUrl = $"/api/files/{work.WorkId}/download"
        };

        return Ok(response);
    }

    [HttpGet("{workId}/meta")]
    [ProducesResponseType(typeof(WorkMetaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public ActionResult<WorkMetaDto> GetWorkMeta(string workId)
    {
        var correlationId = Guid.NewGuid().ToString();
        Request.Headers.TryGetValue("X-Correlation-ID", out var headerCorrelationId);
        var effectiveCorrelationId = headerCorrelationId.FirstOrDefault() ?? correlationId;
        
        _logger.LogInformation("[{CorrelationId}] GET /files/{WorkId}/meta requested", 
            effectiveCorrelationId, workId);

        // Валидация входных данных
        if (string.IsNullOrWhiteSpace(workId))
        {
            _logger.LogWarning("[{CorrelationId}] Empty workId provided", effectiveCorrelationId);
            return CreateErrorResponse(
                "WorkId is required", 
                StatusCodes.Status400BadRequest, 
                effectiveCorrelationId);
        }

        if (!WorkIdHelper.IsValidWorkId(workId))
        {
            _logger.LogWarning("[{CorrelationId}] Invalid workId format: {WorkId}", 
                effectiveCorrelationId, workId);
            return CreateErrorResponse(
                $"Invalid workId format. Expected Guid or 8-character hex, got: {workId}", 
                StatusCodes.Status400BadRequest, 
                effectiveCorrelationId);
        }

        try
        {
            // Поиск работы с использованием нескольких стратегий
            var work = _repository.FindByAnyId(workId);
            
            if (work == null)
            {
                _logger.LogWarning("[{CorrelationId}] Work not found: {WorkId}", 
                    effectiveCorrelationId, workId);
                
                return CreateErrorResponse(
                    $"Work with id '{workId}' not found", 
                    StatusCodes.Status404NotFound, 
                    effectiveCorrelationId);
            }

            // Проверка существования файла на диске
            bool fileExists = false;
            long? fileSize = null;
            DateTime? fileModified = null;

            if (System.IO.File.Exists(work.FilePath))
            {
                try
                {
                    var fileInfo = new FileInfo(work.FilePath);
                    fileExists = true;
                    fileSize = fileInfo.Length;
                    fileModified = fileInfo.LastWriteTimeUtc;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, 
                        "[{CorrelationId}] Failed to get file info for {FilePath}", 
                        effectiveCorrelationId, work.FilePath);
                    // Продолжаем без информации о файле
                }
            }
            else
            {
                _logger.LogWarning("[{CorrelationId}] File not found on disk: {FilePath}", 
                    effectiveCorrelationId, work.FilePath);
            }

            // Формирование ответа
            var response = new WorkMetaDto
            {
                WorkId = WorkIdHelper.GetClientWorkId(work.WorkId),
                StudentId = work.StudentId,
                AssignmentId = work.AssignmentId,
                FileName = work.FileName,
                FileSize = work.FileSize,
                SubmittedAt = work.SubmittedAt,
                FilePath = work.FilePath,
                DownloadUrl = $"/api/files/{work.WorkId}/download",
                PreviewUrl = fileExists && IsTextFile(work.FileName) 
                    ? $"/api/files/{work.WorkId}/download?inline=true" 
                    : null
            };

            _logger.LogInformation("[{CorrelationId}] Successfully retrieved metadata for {WorkId}", 
                effectiveCorrelationId, work.WorkId);

            // Добавляем заголовки для трассировки
            Response.Headers.Append("X-Work-Id", work.WorkId);
            Response.Headers.Append("X-Correlation-ID", effectiveCorrelationId);
            Response.Headers.Append("X-Cache", "MISS");

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{CorrelationId}] Unexpected error retrieving metadata for {WorkId}", 
                effectiveCorrelationId, workId);
            
            return CreateErrorResponse(
                "An unexpected error occurred while retrieving metadata", 
                StatusCodes.Status500InternalServerError, 
                effectiveCorrelationId,
                ex.Message);
        }
    }

    [HttpGet("{workId}/download")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status410Gone)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public IActionResult DownloadFile(string workId, [FromQuery] bool? inline = false)
    {
        var correlationId = Guid.NewGuid().ToString();
        Request.Headers.TryGetValue("X-Correlation-ID", out var headerCorrelationId);
        var effectiveCorrelationId = headerCorrelationId.FirstOrDefault() ?? correlationId;
        
        _logger.LogInformation("[{CorrelationId}] GET /files/{WorkId}/download requested", 
            effectiveCorrelationId, workId);

        // Валидация входных данных
        if (string.IsNullOrWhiteSpace(workId))
        {
            _logger.LogWarning("[{CorrelationId}] Empty workId provided for download", effectiveCorrelationId);
            return CreateErrorResponse(
                "WorkId is required for file download", 
                StatusCodes.Status400BadRequest, 
                effectiveCorrelationId);
        }

        if (!WorkIdHelper.IsValidWorkId(workId))
        {
            _logger.LogWarning("[{CorrelationId}] Invalid workId format for download: {WorkId}", 
                effectiveCorrelationId, workId);
            return CreateErrorResponse(
                $"Invalid workId format. Expected Guid or 8-character hex, got: {workId}", 
                StatusCodes.Status400BadRequest, 
                effectiveCorrelationId);
        }

        try
        {
            // Поиск работы
            var work = _repository.FindByAnyId(workId);
            
            if (work == null)
            {
                _logger.LogWarning("[{CorrelationId}] Work not found for download: {WorkId}", 
                    effectiveCorrelationId, workId);
                
                return CreateErrorResponse(
                    $"Work with id '{workId}' not found", 
                    StatusCodes.Status404NotFound, 
                    effectiveCorrelationId);
            }

            _logger.LogDebug("[{CorrelationId}] Found work: {WorkId}, file path: {FilePath}", 
                effectiveCorrelationId, work.WorkId, work.FilePath);

            // Проверка безопасности пути к файлу
            if (!IsPathSafe(work.FilePath, _storagePath))
            {
                _logger.LogWarning("[{CorrelationId}] Potential path traversal attempt: {FilePath}", 
                    effectiveCorrelationId, work.FilePath);
                
                return CreateErrorResponse(
                    "Access to the requested file is forbidden", 
                    StatusCodes.Status403Forbidden, 
                    effectiveCorrelationId);
            }

            // Проверка существования файла
            if (!System.IO.File.Exists(work.FilePath))
            {
                _logger.LogWarning("[{CorrelationId}] File not found on disk: {FilePath}", 
                    effectiveCorrelationId, work.FilePath);
                
                return CreateErrorResponse(
                    $"File '{work.FileName}' has been deleted or moved", 
                    StatusCodes.Status410Gone, 
                    effectiveCorrelationId);
            }

            // Проверка доступности файла для чтения
            try
            {
                using (var testStream = new FileStream(work.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    // Просто проверяем, что файл можно открыть
                }
            }
            catch (IOException ioEx)
            {
                _logger.LogError(ioEx, "[{CorrelationId}] File is locked or inaccessible: {FilePath}", 
                    effectiveCorrelationId, work.FilePath);
                
                return CreateErrorResponse(
                    "File is currently locked or inaccessible", 
                    StatusCodes.Status423Locked, 
                    effectiveCorrelationId);
            }
            catch (UnauthorizedAccessException authEx)
            {
                _logger.LogError(authEx, "[{CorrelationId}] Unauthorized access to file: {FilePath}", 
                    effectiveCorrelationId, work.FilePath);
                
                return CreateErrorResponse(
                    "Access to the file is denied", 
                    StatusCodes.Status403Forbidden, 
                    effectiveCorrelationId);
            }

            // Получаем информацию о файле
            FileInfo fileInfo;
            try
            {
                fileInfo = new FileInfo(work.FilePath);
                if (!fileInfo.Exists)
                {
                    _logger.LogWarning("[{CorrelationId}] File disappeared after check: {FilePath}", 
                        effectiveCorrelationId, work.FilePath);
                    
                    return CreateErrorResponse(
                        "File is no longer available", 
                        StatusCodes.Status410Gone, 
                        effectiveCorrelationId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{CorrelationId}] Failed to get file info: {FilePath}", 
                    effectiveCorrelationId, work.FilePath);
                
                return CreateErrorResponse(
                    "Failed to access file information", 
                    StatusCodes.Status500InternalServerError, 
                    effectiveCorrelationId);
            }

            // Определяем Content-Type
            var contentType = GetContentType(work.FileName);
            var isTextFile = IsTextFile(work.FileName);
            
            // Формируем заголовок Content-Disposition
            string contentDisposition;
            var encodedFileName = Uri.EscapeDataString(work.FileName);
            
            if (inline == true && isTextFile)
            {
                // Для текстовых файлов - inline
                contentDisposition = $"inline; filename=\"{encodedFileName}\"";
            }
            else
            {
                // Для остальных файлов - attachment
                contentDisposition = $"attachment; filename=\"{encodedFileName}\"";
            }

            // Поддержка Unicode в именах файлов (RFC 5987)
            if (ContainsNonAscii(work.FileName))
            {
                // Кодируем в соответствии с RFC 5987
                var rfc5987Encoded = $"UTF-8''{Uri.EscapeDataString(work.FileName)}";
                contentDisposition += $"; filename*={rfc5987Encoded}";
            }

            // Настраиваем заголовки ответа
            Response.Headers.Append("X-Work-Id", work.WorkId);
            Response.Headers.Append("X-Correlation-ID", effectiveCorrelationId);
            Response.Headers.Append("X-File-Size", fileInfo.Length.ToString());
            Response.Headers.Append("X-File-Name", encodedFileName);
            Response.Headers.Append("X-File-Modified", fileInfo.LastWriteTimeUtc.ToString("O"));
            Response.Headers.Append("Content-Disposition", contentDisposition);
            
            if (work.FileSize > 0)
            {
                Response.Headers.Append("Content-Length", work.FileSize.ToString());
            }

            // Кэширование
            if (fileInfo.LastWriteTimeUtc > DateTime.UtcNow.AddDays(-1))
            {
                // Для свежих файлов - короткий кэш
                Response.Headers.Append("Cache-Control", "private, max-age=300");
            }
            else
            {
                // Для старых файлов - дольше
                Response.Headers.Append("Cache-Control", "private, max-age=3600");
            }

            // Логируем успешное начало отдачи файла
            _logger.LogInformation("[{CorrelationId}] Starting file download: {FileName} ({FileSize} bytes)", 
                effectiveCorrelationId, work.FileName, fileInfo.Length);

            // Используем FileStreamResult для потоковой передачи
            var fileStream = new FileStream(work.FilePath, 
                FileMode.Open, 
                FileAccess.Read, 
                FileShare.Read, 
                4096, 
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            
            return new FileStreamResult(fileStream, contentType)
            {
                EnableRangeProcessing = true, // Включаем поддержку докачки
                LastModified = fileInfo.LastWriteTimeUtc,
                EntityTag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{fileInfo.LastWriteTimeUtc.Ticks}\"")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{CorrelationId}] Unexpected error during file download for {WorkId}", 
                effectiveCorrelationId, workId);
            
            return CreateErrorResponse(
                "An unexpected error occurred while downloading the file", 
                StatusCodes.Status500InternalServerError, 
                effectiveCorrelationId,
                ex.Message);
        }
    }

    [HttpGet("{workId}/download/info")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public IActionResult GetFileDownloadInfo(string workId)
    {
        var correlationId = Guid.NewGuid().ToString();
        
        try
        {
            var work = _repository.FindByAnyId(workId);
            if (work == null)
                return NotFound(new { error = "Work not found", correlationId });

            var fileExists = System.IO.File.Exists(work.FilePath);
            var fileInfo = fileExists ? new FileInfo(work.FilePath) : null;

            var response = new
            {
                workId = WorkIdHelper.GetClientWorkId(work.WorkId),
                originalWorkId = work.WorkId,
                fileName = work.FileName,
                fileSize = work.FileSize,
                fileExists,
                actualFileSize = fileInfo?.Length,
                lastModified = fileInfo?.LastWriteTimeUtc,
                contentType = GetContentType(work.FileName),
                downloadUrl = $"/api/files/{work.WorkId}/download",
                directDownloadUrl = $"/api/files/{work.WorkId}/download?inline=false",
                previewUrl = IsTextFile(work.FileName) ? $"/api/files/{work.WorkId}/download?inline=true" : null,
                correlationId,
                timestamp = DateTime.UtcNow
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{CorrelationId}] Error getting file info for {WorkId}", correlationId, workId);
            return StatusCode(500, new { error = "Failed to get file information", correlationId });
        }
    }

    [HttpGet("assignment/{assignmentId}")]
    public ActionResult<IEnumerable<WorkResponseDto>> GetWorksByAssignment(int assignmentId)
    {
        var works = _repository.GetByAssignmentId(assignmentId);
        var response = works.Select(w => new WorkResponseDto
        {
            WorkId = WorkIdHelper.GetClientWorkId(w.WorkId),
            StudentId = w.StudentId,
            AssignmentId = w.AssignmentId,
            SubmittedAt = w.SubmittedAt,
            FilePath = w.FilePath,
            FileUrl = $"/api/files/{w.WorkId}/download"
        }).ToList();

        return Ok(response);
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        try
        {
            var worksCount = _repository.Count();
            var storageInfo = new DirectoryInfo(_storagePath);
            var storageExists = storageInfo.Exists;
            var storageFiles = storageExists ? storageInfo.GetFiles().Length : 0;
            var storageSize = storageExists 
                ? storageInfo.GetFiles().Sum(f => f.Length) 
                : 0;

            var healthInfo = new
            {
                status = "healthy",
                timestamp = DateTime.UtcNow,
                service = "FileStoringService",
                version = GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0",
                metrics = new
                {
                    worksInMemory = worksCount,
                    storagePath = _storagePath,
                    storageExists,
                    filesOnDisk = storageFiles,
                    totalStorageSize = storageSize
                },
                dependencies = new
                {
                    fileSystem = storageExists ? "healthy" : "unhealthy",
                    memory = "healthy"
                }
            };

            return Ok(healthInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                status = "unhealthy",
                timestamp = DateTime.UtcNow,
                error = ex.Message
            });
        }
    }

    [HttpGet("preview/{workId}")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType)]
    public IActionResult PreviewFile(string workId, [FromQuery] int maxLines = 50)
    {
        var correlationId = Guid.NewGuid().ToString();
        
        try
        {
            var work = _repository.FindByAnyId(workId);
            if (work == null)
                return NotFound();

            if (!System.IO.File.Exists(work.FilePath))
                return NotFound();

            if (!IsTextFile(work.FileName))
            {
                return StatusCode(StatusCodes.Status415UnsupportedMediaType, new
                {
                    error = "File type not supported for preview",
                    fileName = work.FileName,
                    correlationId
                });
            }

            var lines = System.IO.File.ReadLines(work.FilePath)
                .Take(maxLines)
                .ToList();

            return Ok(new
            {
                content = string.Join(Environment.NewLine, lines),
                totalLines = lines.Count,
                truncated = lines.Count >= maxLines,
                fileName = work.FileName,
                correlationId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{CorrelationId}] Failed to preview file {WorkId}", correlationId, workId);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = "Failed to preview file",
                correlationId,
                details = ex.Message
            });
        }
    }

    #region Вспомогательные методы

    private bool IsTextFile(string fileName)
    {
        var textExtensions = new[] 
        { 
            ".txt", ".cs", ".java", ".py", ".js", ".ts", 
            ".html", ".css", ".json", ".xml", ".md", ".yml", ".yaml",
            ".csv", ".log", ".ini", ".config", ".sql", ".sh", ".bat", ".ps1"
        };
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return textExtensions.Contains(ext);
    }

    private string GetContentType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        
        // Словарь MIME-типов
        var mimeTypes = new Dictionary<string, string>
        {
            // Текстовые форматы
            [".txt"] = "text/plain; charset=utf-8",
            [".csv"] = "text/csv; charset=utf-8",
            [".html"] = "text/html; charset=utf-8",
            [".htm"] = "text/html; charset=utf-8",
            [".css"] = "text/css; charset=utf-8",
            [".js"] = "application/javascript; charset=utf-8",
            [".json"] = "application/json; charset=utf-8",
            [".xml"] = "application/xml; charset=utf-8",
            [".md"] = "text/markdown; charset=utf-8",
            
            // Документы
            [".pdf"] = "application/pdf",
            [".doc"] = "application/msword",
            [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            [".xls"] = "application/vnd.ms-excel",
            [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            [".ppt"] = "application/vnd.ms-powerpoint",
            [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            
            // Изображения
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".gif"] = "image/gif",
            [".bmp"] = "image/bmp",
            [".svg"] = "image/svg+xml",
            
            // Архивы
            [".zip"] = "application/zip",
            [".rar"] = "application/vnd.rar",
            [".7z"] = "application/x-7z-compressed",
            [".tar"] = "application/x-tar",
            [".gz"] = "application/gzip",
            
            // Исходный код
            [".cs"] = "text/plain; charset=utf-8",
            [".java"] = "text/plain; charset=utf-8",
            [".py"] = "text/plain; charset=utf-8",
            [".cpp"] = "text/plain; charset=utf-8",
            [".h"] = "text/plain; charset=utf-8",
            [".sql"] = "text/plain; charset=utf-8",
            
            // По умолчанию
            [""] = "application/octet-stream"
        };

        return mimeTypes.TryGetValue(ext, out var mimeType) ? mimeType : "application/octet-stream";
    }

    private bool IsPathSafe(string filePath, string baseDirectory)
    {
        try
        {
            // Получаем полный путь и нормализуем его
            var fullPath = Path.GetFullPath(filePath);
            var baseFullPath = Path.GetFullPath(baseDirectory);
            
            // Проверяем, что путь находится внутри базовой директории
            return fullPath.StartsWith(baseFullPath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private bool ContainsNonAscii(string text)
    {
        return text.Any(c => c > 127);
    }

    private ObjectResult CreateErrorResponse(string title, int statusCode, string correlationId, string? detail = null)
    {
        var problemDetails = new ProblemDetails
        {
            Title = title,
            Status = statusCode,
            Detail = detail,
            Instance = $"/api/files/{{workId}}/download",
            Extensions = new Dictionary<string, object?>
            {
                ["correlationId"] = correlationId,
                ["timestamp"] = DateTime.UtcNow,
                ["workId"] = Request.RouteValues["workId"]
            }
        };

        Response.Headers.Append("X-Correlation-ID", correlationId);
        Response.Headers.Append("X-Error-Type", title.Replace(" ", "-").ToLower());

        return new ObjectResult(problemDetails)
        {
            StatusCode = statusCode,
            ContentTypes = { "application/problem+json" }
        };
    }

    #endregion
}