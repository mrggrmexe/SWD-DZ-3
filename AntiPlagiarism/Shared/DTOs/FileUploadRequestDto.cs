namespace Shared.DTOs;

/// <summary>
/// Логическая модель запроса на загрузку работы.
/// Для multipart/form-data обычно не используется напрямую,
/// но может пригодиться для JSON-ручек или тестов.
/// </summary>
public class FileUploadRequestDto
{
    /// <summary>
    /// Идентификатор студента.
    /// </summary>
    public string StudentId { get; set; } = null!;

    /// <summary>
    /// Идентификатор задания / контрольной работы.
    /// </summary>
    public string AssignmentId { get; set; } = null!;

    /// <summary>
    /// Имя файла (для справки / логирования).
    /// </summary>
    public string? FileName { get; set; }

    /// <summary>
    /// (Опционально) MIME-тип файла.
    /// </summary>
    public string? ContentType { get; set; }
}