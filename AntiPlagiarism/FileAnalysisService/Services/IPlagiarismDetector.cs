using System;
using System.Threading.Tasks;
using FileAnalysisService.Data;
using FileAnalysisService.Models;
using Shared.DTOs;

namespace FileAnalysisService.Services
{
    /// <summary>
    /// Интерфейс для детектора плагиата
    /// </summary>
    public interface IPlagiarismDetector
    {
        /// <summary>
        /// Проверяет работу на плагиат
        /// </summary>
        /// <param name="workMeta">Метаданные проверяемой работы</param>
        /// <param name="context">Контекст базы данных</param>
        /// <returns>Результат проверки</returns>
        Task<PlagiarismCheckResult> CheckForPlagiarismAsync(WorkMetaDto workMeta, AppDbContext context);

        /// <summary>
        /// Проверяет работу на плагиат с предоставленным контентом
        /// </summary>
        /// <param name="workMeta">Метаданные проверяемой работы</param>
        /// <param name="workContent">Контент работы</param>
        /// <param name="context">Контекст базы данных</param>
        /// <returns>Результат проверки</returns>
        Task<PlagiarismCheckResult> CheckForPlagiarismWithContentAsync(
            WorkMetaDto workMeta, 
            string? workContent, 
            AppDbContext context);
    }
}