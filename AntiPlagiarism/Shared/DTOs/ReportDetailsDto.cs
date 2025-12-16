using System.Text.Json.Serialization;

namespace Shared.DTOs;

public class ReportDetailsDto
{
    [JsonPropertyName("reportId")]
    public int ReportId { get; set; }
    
    [JsonPropertyName("workId")]
    public int WorkId { get; set; }
    
    [JsonPropertyName("studentId")]
    public int StudentId { get; set; }
    
    [JsonPropertyName("assignmentId")]
    public int AssignmentId { get; set; }
    
    [JsonPropertyName("isPlagiarism")]
    public bool IsPlagiarism { get; set; }
    
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
    
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }
    
    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; set; }
    
    [JsonPropertyName("wordCloudUrl")]
    public string? WordCloudUrl { get; set; }
    
    [JsonPropertyName("details")]
    public string? Details { get; set; }
    
    [JsonPropertyName("similarityScore")]
    public double SimilarityScore { get; set; }
}