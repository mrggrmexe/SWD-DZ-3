using System.Text.Json.Serialization;

namespace Shared.DTOs;

public class AnalysisResponseDto
{
    [JsonPropertyName("reportId")]
    public int ReportId { get; set; }
    
    [JsonPropertyName("workId")]
    public int WorkId { get; set; }
    
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
    
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
    
    [JsonPropertyName("estimatedCompletion")]
    public DateTime? EstimatedCompletion { get; set; }
}