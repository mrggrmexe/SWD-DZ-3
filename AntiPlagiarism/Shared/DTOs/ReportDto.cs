namespace Shared.DTOs;

/// <summary>
/// Базовый DTO отчёта по проверке работы на плагиат.
/// </summary>
public class ReportDto
{
    /// <summary>
    /// Идентификатор отчёта.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Идентификатор сдачи работы, к которой относится отчёт.
    /// </summary>
    public int WorkId { get; set; }

    /// <summary>
    /// Признак того, что обнаружены признаки плагиата.
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