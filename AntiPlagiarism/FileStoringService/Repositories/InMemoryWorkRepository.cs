using System.Collections.Concurrent;
using FileStoringService.Models;

namespace FileStoringService.Repositories;

/// <summary>
/// Простая in-memory реализация репозитория сдач.
/// Потокобезопасна за счёт ConcurrentDictionary + Interlocked.
/// </summary>
public class InMemoryWorkRepository : IWorkRepository
{
    private readonly ConcurrentDictionary<int, Work> _storage = new();
    private int _nextId = 0;

    public Task<Work> AddAsync(Work work, CancellationToken cancellationToken = default)
    {
        // Присваиваем новый Id
        var id = Interlocked.Increment(ref _nextId);
        work.Id = id;

        if (!_storage.TryAdd(id, work))
        {
            // Это очень маловероятно, но на всякий случай.
            throw new InvalidOperationException($"Не удалось добавить работу с Id={id} в хранилище.");
        }

        return Task.FromResult(work);
    }

    public Task<Work?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        _storage.TryGetValue(id, out var work);
        return Task.FromResult(work);
    }

    public Task<IReadOnlyList<Work>> GetByAssignmentAsync(
        string assignmentId,
        CancellationToken cancellationToken = default)
    {
        var result = _storage.Values
            .Where(w => w.AssignmentId == assignmentId)
            .OrderBy(w => w.SubmittedAt)
            .ToList()
            .AsReadOnly();

        return Task.FromResult((IReadOnlyList<Work>)result);
    }
}