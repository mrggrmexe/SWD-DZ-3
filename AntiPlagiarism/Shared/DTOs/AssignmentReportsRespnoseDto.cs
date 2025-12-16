using System.Text.Json.Serialization;

namespace Shared.DTOs;

public class AssignmentReportsResponseDto
{
    [JsonPropertyName("assignmentId")]
    public int AssignmentId { get; set; }
    
    [JsonPropertyName("reports")]
    public List<ReportSummaryDto> Reports { get; set; } = new();
    
    // Опциональные поля - добавьте если нужны
    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }
    
    [JsonPropertyName("plagiarismCount")]
    public int PlagiarismCount { get; set; }
}