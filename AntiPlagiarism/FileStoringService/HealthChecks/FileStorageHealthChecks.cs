using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FileStoringService.HealthChecks;

public class FileStorageHealthCheck : IHealthCheck
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<FileStorageHealthCheck> _logger;

    public FileStorageHealthCheck(IConfiguration configuration, ILogger<FileStorageHealthCheck> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, 
        CancellationToken cancellationToken = default)
    {
        var storagePath = _configuration["Storage:Path"] ?? "storage";
        
        try
        {
            // Проверяем доступность директории
            if (!Directory.Exists(storagePath))
            {
                Directory.CreateDirectory(storagePath);
                _logger.LogWarning("Storage directory created: {Path}", storagePath);
            }

            // Проверяем возможность записи
            var testFile = Path.Combine(storagePath, $"healthcheck_{Guid.NewGuid()}.tmp");
            await File.WriteAllTextAsync(testFile, DateTime.UtcNow.ToString(), cancellationToken);
            File.Delete(testFile);

            // Проверяем свободное место
            var drive = new DriveInfo(Path.GetPathRoot(storagePath) ?? storagePath);
            var freeSpacePercentage = (double)drive.AvailableFreeSpace / drive.TotalSize * 100;

            var data = new Dictionary<string, object>
            {
                ["storagePath"] = storagePath,
                ["totalSpace"] = drive.TotalSize,
                ["freeSpace"] = drive.AvailableFreeSpace,
                ["freeSpacePercentage"] = freeSpacePercentage,
                ["isReady"] = drive.IsReady
            };

            return freeSpacePercentage > 10
                ? HealthCheckResult.Healthy("Storage is healthy", data)
                : HealthCheckResult.Degraded("Storage space is low", data: data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Storage health check failed");
            return HealthCheckResult.Unhealthy("Storage check failed", ex, new Dictionary<string, object>
            {
                ["storagePath"] = storagePath
            });
        }
    }
}