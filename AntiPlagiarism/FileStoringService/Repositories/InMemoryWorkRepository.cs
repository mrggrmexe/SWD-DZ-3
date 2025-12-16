using FileStoringService.Helpers;
using FileStoringService.Models;

namespace FileStoringService.Repositories;

public class InMemoryWorkRepository : IWorkRepository
{
    private readonly Dictionary<string, Work> _works = new();
    private readonly Dictionary<string, string> _normalizedIds = new(); // Кэш нормализованных ID
    private readonly ILogger<InMemoryWorkRepository> _logger;
    private readonly object _lock = new();

    public InMemoryWorkRepository(ILogger<InMemoryWorkRepository> logger)
    {
        _logger = logger;
    }

    public void Add(Work work)
    {
        if (string.IsNullOrWhiteSpace(work.WorkId))
            throw new ArgumentException("WorkId cannot be null or empty", nameof(work));

        lock (_lock)
        {
            // Нормализуем ID для поиска
            var normalizedId = WorkIdHelper.NormalizeWorkId(work.WorkId);
            
            if (_works.ContainsKey(work.WorkId))
            {
                _logger.LogWarning("Work with ID {WorkId} already exists. Overwriting.", work.WorkId);
            }

            _works[work.WorkId] = work;
            
            // Сохраняем нормализованный ID для поиска
            if (!string.IsNullOrEmpty(normalizedId) && normalizedId != work.WorkId)
            {
                _normalizedIds[normalizedId] = work.WorkId;
            }

            _logger.LogInformation("Work added: {WorkId} for student {StudentId}, assignment {AssignmentId}",
                work.WorkId, work.StudentId, work.AssignmentId);
        }
    }

    public Work? GetById(string workId)
    {
        if (string.IsNullOrWhiteSpace(workId))
            return null;

        lock (_lock)
        {
            return _works.TryGetValue(workId, out var work) ? work : null;
        }
    }

    public Work? GetByNormalizedId(string normalizedWorkId)
    {
        if (string.IsNullOrWhiteSpace(normalizedWorkId))
            return null;

        lock (_lock)
        {
            // Пытаемся найти через кэш нормализованных ID
            if (_normalizedIds.TryGetValue(normalizedWorkId, out var originalWorkId))
            {
                return GetById(originalWorkId);
            }

            return null;
        }
    }

    public Work? FindByAnyId(string workId)
    {
        if (string.IsNullOrWhiteSpace(workId))
            return null;

        // 1. Пробуем найти по оригинальному ID
        var work = GetById(workId);
        if (work != null)
        {
            _logger.LogDebug("Work found by original ID: {WorkId}", workId);
            return work;
        }

        // 2. Пробуем найти по нормализованному ID
        var normalizedId = WorkIdHelper.NormalizeWorkId(workId);
        work = GetByNormalizedId(normalizedId);
        if (work != null)
        {
            _logger.LogDebug("Work found by normalized ID: {NormalizedId} -> {OriginalId}", 
                normalizedId, work.WorkId);
            return work;
        }

        // 3. Линейный поиск по всем работам (fallback)
        lock (_lock)
        {
            work = _works.Values.FirstOrDefault(w => 
                w.WorkId.Equals(workId, StringComparison.OrdinalIgnoreCase) ||
                w.WorkId.Replace("-", "").StartsWith(workId.Replace("-", "")) ||
                WorkIdHelper.GetClientWorkId(w.WorkId).ToString() == workId);

            if (work != null)
            {
                _logger.LogDebug("Work found by fallback search: {WorkId}", workId);
            }
        }

        return work;
    }

    public IEnumerable<Work> GetByAssignmentId(int assignmentId)
    {
        lock (_lock)
        {
            return _works.Values
                .Where(w => w.AssignmentId == assignmentId)
                .OrderByDescending(w => w.SubmittedAt)
                .ToList();
        }
    }

    public IEnumerable<Work> GetByStudentId(int studentId)
    {
        lock (_lock)
        {
            return _works.Values
                .Where(w => w.StudentId == studentId)
                .OrderByDescending(w => w.SubmittedAt)
                .ToList();
        }
    }

    public IEnumerable<Work> GetAll()
    {
        lock (_lock)
        {
            return _works.Values.ToList();
        }
    }

    public bool Exists(string workId)
    {
        if (string.IsNullOrWhiteSpace(workId))
            return false;

        lock (_lock)
        {
            return _works.ContainsKey(workId) || 
                   _normalizedIds.ContainsKey(WorkIdHelper.NormalizeWorkId(workId));
        }
    }

    public int Count()
    {
        lock (_lock)
        {
            return _works.Count;
        }
    }

    public int CountByAssignment(int assignmentId)
    {
        lock (_lock)
        {
            return _works.Values.Count(w => w.AssignmentId == assignmentId);
        }
    }

    public void Remove(string workId)
    {
        if (string.IsNullOrWhiteSpace(workId))
            return;

        lock (_lock)
        {
            if (_works.Remove(workId, out var removedWork))
            {
                // Удаляем из кэша нормализованных ID
                var normalizedId = WorkIdHelper.NormalizeWorkId(workId);
                _normalizedIds.Remove(normalizedId);
                
                _logger.LogInformation("Work removed: {WorkId}", workId);
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            int count = _works.Count;
            _works.Clear();
            _normalizedIds.Clear();
            _logger.LogInformation("Repository cleared. Removed {Count} works.", count);
        }
    }
}