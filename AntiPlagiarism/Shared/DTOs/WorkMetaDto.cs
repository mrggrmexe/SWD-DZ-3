using System.Text.Json.Serialization;

namespace Shared.DTOs;

public class WorkMetaDto
{
    [JsonPropertyName("workId")]
    public int WorkId { get; set; }
    
    [JsonPropertyName("originalWorkId")]
    public string OriginalWorkId { get; set; } = string.Empty;
    
    [JsonPropertyName("studentId")]
    public int StudentId { get; set; }
    
    [JsonPropertyName("assignmentId")]
    public int AssignmentId { get; set; }
    
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;
    
    [JsonPropertyName("fileSize")]
    public long FileSize { get; set; }
    
    [JsonPropertyName("currentFileSize")]
    public long? CurrentFileSize { get; set; }
    
    [JsonPropertyName("fileExists")]
    public bool FileExists { get; set; }
    
    [JsonPropertyName("fileModified")]
    public DateTime? FileModified { get; set; }
    
    [JsonPropertyName("submittedAt")]
    public DateTime SubmittedAt { get; set; }
    
    [JsonPropertyName("storagePath")]
    public string StoragePath { get; set; } = string.Empty;
    
    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; set; } = string.Empty;
    
    [JsonPropertyName("previewUrl")]
    public string? PreviewUrl { get; set; }
    
    [JsonPropertyName("formattedFileSize")]
    public string FormattedFileSize => FormatFileSize(FileSize);
    
    [JsonPropertyName("age")]
    public string Age => GetAgeString();
    
    [JsonPropertyName("metadata")]
    public Dictionary<string, object> Metadata { get; set; } = new();

    private string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    private string GetAgeString()
    {
        var age = DateTime.UtcNow - SubmittedAt;
        
        if (age.TotalDays >= 365)
            return $"{age.TotalDays / 365:0} years ago";
        if (age.TotalDays >= 30)
            return $"{age.TotalDays / 30:0} months ago";
        if (age.TotalDays >= 1)
            return $"{age.TotalDays:0} days ago";
        if (age.TotalHours >= 1)
            return $"{age.TotalHours:0} hours ago";
        if (age.TotalMinutes >= 1)
            return $"{age.TotalMinutes:0} minutes ago";
        
        return "just now";
    }
}