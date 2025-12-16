using System.Text.Json.Serialization;

namespace Shared.DTOs;

public class UploadWorkResponseDto
{
    [JsonPropertyName("workId")]
    public int WorkId { get; set; }
    
    [JsonPropertyName("studentId")]
    public int StudentId { get; set; }
    
    [JsonPropertyName("assignmentId")]
    public int AssignmentId { get; set; }
    
    [JsonPropertyName("submittedAt")]
    public DateTime SubmittedAt { get; set; }
    
    [JsonPropertyName("analysisStarted")]
    public bool AnalysisStarted { get; set; }
    
    [JsonPropertyName("error")]
    public string? Error { get; set; }
    
    [JsonPropertyName("fileUrl")]
    public string? FileUrl { get; set; }
    
    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; set; }
}