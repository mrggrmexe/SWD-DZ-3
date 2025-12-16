using System.ComponentModel.DataAnnotations;

namespace FileStoringService.Models;

public class Work
{
    [Key]
    public string WorkId { get; set; } = string.Empty;
    
    [Required]
    public int StudentId { get; set; }
    
    [Required]
    public int AssignmentId { get; set; }
    
    [Required]
    public string FileName { get; set; } = string.Empty;
    
    [Required]
    public string FilePath { get; set; } = string.Empty;
    
    [Required]
    public long FileSize { get; set; }
    
    [Required]
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    
    // Дополнительные метаданные
    public string? ContentType { get; set; }
    public string? Checksum { get; set; }
    public string? OriginalFileName { get; set; }
    public string? UploadedBy { get; set; }
    public string? UserAgent { get; set; }
    public string? ClientIp { get; set; }
    
    // Статусы
    public bool IsArchived { get; set; } = false;
    public DateTime? ArchivedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    
    // Теги для поиска
    public List<string> Tags { get; set; } = new();
    
    // Методы для бизнес-логики
    public bool IsActive() => DeletedAt == null;
    public TimeSpan Age() => DateTime.UtcNow - SubmittedAt;
    
    public string GetFormattedFileSize()
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = FileSize;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}