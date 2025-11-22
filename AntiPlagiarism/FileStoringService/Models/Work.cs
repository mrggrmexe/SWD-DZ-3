namespace FileStoringService.Models;

/// <summary>
/// Доменная сущность: одна сдача работы (submission).
/// </summary>
public class Work
{
    /// <summary>
    /// Идентификатор сдачи (генерируется при сохранении).
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Идентификатор студента.
    /// </summary>
    public string StudentId { get; set; } = null!;

    /// <summary>
    /// Идентификатор задания / контрольной работы.
    /// </summary>
    public string AssignmentId { get; set; } = null!;

    /// <summary>
    /// Время сдачи (UTC).
    /// </summary>
    public DateTime SubmittedAt { get; set; }

    /// <summary>
    /// Путь к файлу на сервере (или логический идентификатор).
    /// </summary>
    public string FilePath { get; set; } = null!;
}