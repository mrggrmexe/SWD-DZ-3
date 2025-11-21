namespace Shared.DTOs;

/// <summary>
/// Полное описание сдачи работы (submission).
/// Используется как базовый DTO для представления работы.
/// </summary>
public class WorkDto
{
    /// <summary>
    /// Идентификатор сдачи (work / submission).
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
    /// Время сдачи работы (UTC).
    /// </summary>
    public DateTime SubmittedAt { get; set; }

    /// <summary>
    /// Путь к файлу на сервере (или логический идентификатор файла).
    /// </summary>
    public string FilePath { get; set; } = null!;
}