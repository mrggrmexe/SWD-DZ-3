using FileStoringService.Models;

namespace FileStoringService.Repositories;

/// <summary>
/// Абстракция хранилища сдач работ.
/// Позволяет легко подменить in-memory на EF Core.
/// </summary>
public interface IWorkRepository
{
    /// <summary>
    /// Добавить новую сдачу.
    /// Id присваивается при сохранении.
    /// </summary>
    Task<Work> AddAsync(Work work, CancellationToken cancellationToken = default);

    /// <summary>
    /// Найти сдачу по идентификатору.
    /// </summary>
    Task<Work?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Получить все сдачи по заданию (опционально, на будущее).
    /// </summary>
    Task<IReadOnlyList<Work>> GetByAssignmentAsync(
        string assignmentId,
        CancellationToken cancellationToken = default);
}