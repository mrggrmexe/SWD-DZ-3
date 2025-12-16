using System.Text.Json.Serialization;

namespace Shared.DTOs;

public class WorkResponseDto
{
    [JsonPropertyName("workId")]
    public int WorkId { get; set; }
    
    [JsonPropertyName("studentId")]
    public int StudentId { get; set; }
    
    [JsonPropertyName("assignmentId")]
    public int AssignmentId { get; set; }
    
    [JsonPropertyName("submittedAt")]
    public DateTime SubmittedAt { get; set; }
    
    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = string.Empty;
    
    [JsonPropertyName("fileUrl")]
    public string FileUrl { get; set; } = string.Empty;
}