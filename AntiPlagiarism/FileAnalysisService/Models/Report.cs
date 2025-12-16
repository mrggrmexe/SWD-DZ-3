using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FileAnalysisService.Models
{
    public class Report
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int WorkId { get; set; }

        [Required]
        public int StudentId { get; set; }

        [Required]
        public int AssignmentId { get; set; }

        public bool IsPlagiarism { get; set; }

        [Required]
        public ReportStatus Status { get; set; } = ReportStatus.Pending;

        [Required]
        public DateTime CreatedAt { get; set; }

        public DateTime? CompletedAt { get; set; }

        [MaxLength(500)]
        public string? WordCloudUrl { get; set; }

        public string? Details { get; set; }

        public string? PlagiarismSources { get; set; }

        public double? AnalysisDuration { get; set; }

        [NotMapped]
        public List<PlagiarismSource>? ParsedSources
        {
            get => string.IsNullOrEmpty(PlagiarismSources) 
                ? null 
                : System.Text.Json.JsonSerializer.Deserialize<List<PlagiarismSource>>(PlagiarismSources);
            set => PlagiarismSources = value == null 
                ? null 
                : System.Text.Json.JsonSerializer.Serialize(value);
        }
    }

    public enum ReportStatus
    {
        Pending = 0,
        Done = 1,
        Error = 2,
        Processing = 3
    }

    public class PlagiarismSource
    {
        public int SourceWorkId { get; set; }
        public int SourceStudentId { get; set; }
        public DateTime SourceSubmittedAt { get; set; }
        public string? Reason { get; set; }
        public double SimilarityPercentage { get; set; }
    }

    public class PlagiarismCheckResult
    {
        public bool IsPlagiarism { get; set; }
        public string Details { get; set; } = string.Empty;
        public List<PlagiarismSource> Sources { get; set; } = new();
        public int TotalCheckedWorks { get; set; }
    }

    public class WorkContent
    {
        public int WorkId { get; set; }
        public string? FilePath { get; set; }
        public string? FileName { get; set; }
        public string? Content { get; set; }
        public DateTime SubmittedAt { get; set; }
        public int StudentId { get; set; }
        public int AssignmentId { get; set; }
    }
}