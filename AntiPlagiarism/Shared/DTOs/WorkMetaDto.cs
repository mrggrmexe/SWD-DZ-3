using System.Text.Json.Serialization;

namespace Shared.DTOs;

public class WorkMetaDto
{
    [JsonPropertyName("workId")]
    public int WorkId { get; set; }
    
    [JsonPropertyName("studentId")]
    public int StudentId { get; set; }
    
    [JsonPropertyName("assignmentId")]
    public int AssignmentId { get; set; }
    
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;
    
    [JsonPropertyName("fileSize")]
    public long FileSize { get; set; }
    
    [JsonPropertyName("submittedAt")]
    public DateTime SubmittedAt { get; set; }
    
    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = string.Empty;  // Добавлено!
    
    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; set; } = string.Empty;
    
    [JsonPropertyName("previewUrl")]
    public string? PreviewUrl { get; set; }
}