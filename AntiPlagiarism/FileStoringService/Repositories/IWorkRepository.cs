using FileStoringService.Models;

namespace FileStoringService.Repositories;

public interface IWorkRepository
{
    void Add(Work work);
    Work? GetById(string workId);
    Work? GetByNormalizedId(string normalizedWorkId);
    IEnumerable<Work> GetByAssignmentId(int assignmentId);
    IEnumerable<Work> GetByStudentId(int studentId);
    
    // Новые методы для поиска по разным форматам
    Work? FindByAnyId(string workId);
    bool Exists(string workId);
    IEnumerable<Work> GetAll();
    
    // Методы для статистики
    int Count();
    int CountByAssignment(int assignmentId);
    
    // Методы для очистки (опционально)
    void Remove(string workId);
    void Clear();
}