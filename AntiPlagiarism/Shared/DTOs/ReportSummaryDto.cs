namespace Shared.DTOs;

/// <summary>
/// Краткая информация об отчёте по проверке.
/// Используется в списках отчётов (например, по заданию).
/// </summary>
public class ReportSummaryDto
{
    /// <summary>
    /// Идентификатор отчёта.
    /// </summary>
    public int ReportId { get; set; }

    /// <summary>
    /// Идентификатор конкретной сдачи (work / submission),
    /// к которой относится этот отчёт.
    /// </summary>
    public int WorkId { get; set; }

    /// <summary>
    /// Идентификатор студента, который сдавал работу.
    /// </summary>
    public string StudentId { get; set; } = null!;

    /// <summary>
    /// true, если обнаружены признаки плагиата.
    /// </summary>
    public bool IsPlagiarism { get; set; }

    /// <summary>
    /// Статус анализа: Pending / Done / Error.
    /// </summary>
    public string Status { get; set; } = null!;

    /// <summary>
    /// Время создания отчёта (UTC).
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// (Опционально) URL на облако слов по этой работе.
    /// </summary>
    public string? WordCloudUrl { get; set; }
}