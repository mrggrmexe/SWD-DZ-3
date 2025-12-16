namespace FileAnalysisService.Configuration;

public class ServiceSettings
{
    public string FileStoringServiceUrl { get; set; } = "http://file-storing-service:8080";
    public string GatewayUrl { get; set; } = "http://gateway:8080";
    public int MaxConcurrentAnalyses { get; set; } = 10;
    public int AnalysisTimeoutSeconds { get; set; } = 30;
    public int RetryCount { get; set; } = 3;
}