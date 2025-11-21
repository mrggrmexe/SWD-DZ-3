namespace Shared.DTOs;

/// <summary>
/// Ответ Gateway / FileAnalysisService при запросе
/// отчётов по конкретному заданию (контрольной работе).
/// </summary>
public class AssignmentReportsResponseDto
{
    /// <summary>
    /// Идентификатор задания (контрольной работы).
    /// </summary>
    public string AssignmentId { get; set; } = null!;

    /// <summary>
    /// Список отчётов по всем сдачам этого задания.
    /// </summary>
    public List<ReportSummaryDto> Reports { get; set; } = new();
}