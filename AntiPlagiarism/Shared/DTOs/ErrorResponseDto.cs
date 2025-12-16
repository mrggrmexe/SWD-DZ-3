using System.Text.Json.Serialization;

namespace Shared.DTOs;

public class ErrorResponseDto
{
    [JsonPropertyName("requestId")]
    public string RequestId { get; set; } = string.Empty;
    
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
    
    [JsonPropertyName("details")]
    public string Details { get; set; } = string.Empty;
    
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}