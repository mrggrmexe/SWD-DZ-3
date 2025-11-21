namespace Shared.DTOs;

/// <summary>
/// Метаданные о конкретной сдаче работы (без содержимого файла).
/// </summary>
public class WorkMetaDto
{
    /// <summary>
    /// Идентификатор сдачи (work / submission).
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
    /// Путь к файлу на сервере (или логический идентификатор).
    /// </summary>
    public string FilePath { get; set; } = null!;
}