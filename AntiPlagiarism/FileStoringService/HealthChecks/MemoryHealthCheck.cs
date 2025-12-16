using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Diagnostics;

namespace FileStoringService.HealthChecks;

public class MemoryHealthCheck : IHealthCheck
{
    private readonly ILogger<MemoryHealthCheck> _logger;
    private readonly long _memoryThreshold = 100 * 1024 * 1024; // 100 MB

    public MemoryHealthCheck(ILogger<MemoryHealthCheck> logger)
    {
        _logger = logger;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var process = Process.GetCurrentProcess();
            var memoryUsed = process.WorkingSet64;
            var memoryPercent = (double)memoryUsed / _memoryThreshold * 100;

            var data = new Dictionary<string, object>
            {
                ["processId"] = process.Id,
                ["memoryUsedBytes"] = memoryUsed,
                ["memoryUsedMB"] = Math.Round(memoryUsed / (1024.0 * 1024.0), 2),
                ["memoryThresholdMB"] = _memoryThreshold / (1024 * 1024),
                ["memoryPercentage"] = Math.Round(memoryPercent, 2),
                ["threadCount"] = process.Threads.Count,
                ["startTime"] = process.StartTime
            };

            if (memoryPercent > 90)
            {
                _logger.LogWarning("High memory usage detected: {MemoryPercent}%", memoryPercent);
                return Task.FromResult(
                    HealthCheckResult.Degraded("High memory usage", data: data));
            }

            return Task.FromResult(
                HealthCheckResult.Healthy("Memory usage is normal", data));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Memory health check failed");
            return Task.FromResult(
                HealthCheckResult.Unhealthy("Failed to check memory", ex));
        }
    }
}