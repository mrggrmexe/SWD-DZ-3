namespace Shared.DTOs;

/// <summary>
/// Детальная информация об отчёте по проверке.
/// Наследуется от краткого отчёта и добавляет extra-поля.
/// </summary>
public class ReportDetailsDto : ReportSummaryDto
{
    /// <summary>
    /// Идентификатор задания / контрольной работы.
    /// </summary>
    public string AssignmentId { get; set; } = null!;

    /// <summary>
    /// Подробности анализа (например, почему признано плагиатом).
    /// </summary>
    public string? Details { get; set; }
}