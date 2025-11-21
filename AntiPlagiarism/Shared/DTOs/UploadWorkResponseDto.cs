namespace Shared.DTOs;

/// <summary>
/// Ответ Gateway на загрузку работы.
/// </summary>
public class UploadWorkResponseDto
{
    /// <summary>
    /// Идентификатор конкретной сдачи (submission / work).
    /// </summary>
    public int WorkId { get; set; }

    /// <summary>
    /// Идентификатор задания / контрольной работы.
    /// </summary>
    public string AssignmentId { get; set; } = null!;

    /// <summary>
    /// Идентификатор студента.
    /// </summary>
    public string StudentId { get; set; } = null!;

    /// <summary>
    /// Время сдачи работы (UTC).
    /// </summary>
    public DateTime SubmittedAt { get; set; }

    /// <summary>
    /// Удалось ли запустить анализ работы.
    /// </summary>
    public bool AnalysisStarted { get; set; }

    /// <summary>
    /// Текст ошибки, если анализ не был запущен (например, сервис недоступен).
    /// </summary>
    public string? Error { get; set; }
}