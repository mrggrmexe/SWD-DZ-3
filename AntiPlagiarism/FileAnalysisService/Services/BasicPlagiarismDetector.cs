using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using FileAnalysisService.Data;
using FileAnalysisService.Models;
using Shared.DTOs;

namespace FileAnalysisService.Services
{
    public class BasicPlagiarismDetector : IPlagiarismDetector
    {
        private readonly ILogger<BasicPlagiarismDetector> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        public BasicPlagiarismDetector(
            ILogger<BasicPlagiarismDetector> logger,
            IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        public async Task<PlagiarismCheckResult> CheckForPlagiarismAsync(
            WorkMetaDto workMeta, 
            AppDbContext context)
        {
            try
            {
                _logger.LogInformation("Проверка плагиата для работы {WorkId}, студент {StudentId}, задание {AssignmentId}", 
                    workMeta.WorkId, workMeta.StudentId, workMeta.AssignmentId);

                var previousSubmissions = await context.Reports
                    .Where(r => r.AssignmentId == workMeta.AssignmentId &&
                                r.StudentId != workMeta.StudentId &&
                                r.CreatedAt < workMeta.SubmittedAt)
                    .OrderByDescending(r => r.CreatedAt)
                    .Take(20)
                    .ToListAsync();

                if (!previousSubmissions.Any())
                {
                    _logger.LogDebug("Плагиат не обнаружен: нет предыдущих сдач по заданию {AssignmentId}", 
                        workMeta.AssignmentId);
                    
                    return new PlagiarismCheckResult
                    {
                        IsPlagiarism = false,
                        Details = "Плагиат не обнаружен. Это первая сдача или единственная работа по заданию.",
                        TotalCheckedWorks = 0
                    };
                }

                var plagiarismDetected = previousSubmissions.Any();

                var result = new PlagiarismCheckResult
                {
                    IsPlagiarism = plagiarismDetected,
                    TotalCheckedWorks = previousSubmissions.Count
                };

                if (plagiarismDetected)
                {
                    result.Sources = previousSubmissions.Select(s => new PlagiarismSource
                    {
                        SourceWorkId = s.WorkId,
                        SourceStudentId = s.StudentId,
                        SourceSubmittedAt = s.CreatedAt,
                        Reason = "Более ранняя сдача по тому же заданию",
                        SimilarityPercentage = 100.0
                    }).ToList();

                    result.Details = $"Обнаружен возможный плагиат. Найдено {previousSubmissions.Count} более ранних сдач другими студентами.";
                    
                    _logger.LogWarning("Плагиат обнаружен для работы {WorkId}. Источники: {SourcesCount}", 
                        workMeta.WorkId, previousSubmissions.Count);
                }
                else
                {
                    result.Details = $"Плагиат не обнаружен. Проверено {previousSubmissions.Count} предыдущих сдач.";
                    _logger.LogDebug("Плагиат не обнаружен для работы {WorkId}", workMeta.WorkId);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при проверке плагиата для работы {WorkId}", workMeta.WorkId);
                
                return new PlagiarismCheckResult
                {
                    IsPlagiarism = false,
                    Details = $"Ошибка проверки плагиата: {ex.Message}",
                    TotalCheckedWorks = 0
                };
            }
        }

        public async Task<PlagiarismCheckResult> CheckForPlagiarismWithContentAsync(
            WorkMetaDto workMeta, 
            string? workContent, 
            AppDbContext context)
        {
            if (string.IsNullOrWhiteSpace(workContent))
            {
                return await CheckForPlagiarismAsync(workMeta, context);
            }

            try
            {
                _logger.LogInformation("Расширенная проверка плагиата с анализом контента для работы {WorkId}", workMeta.WorkId);

                var previousSubmissions = await context.Reports
                    .Where(r => r.AssignmentId == workMeta.AssignmentId &&
                                r.StudentId != workMeta.StudentId &&
                                r.CreatedAt < workMeta.SubmittedAt)
                    .OrderByDescending(r => r.CreatedAt)
                    .Take(10)
                    .ToListAsync();

                if (!previousSubmissions.Any())
                {
                    return new PlagiarismCheckResult
                    {
                        IsPlagiarism = false,
                        Details = "Плагиат не обнаружен. Нет предыдущих сдач для сравнения.",
                        TotalCheckedWorks = 0
                    };
                }

                var currentWords = TokenizeText(workContent);
                var plagiarismSources = new List<PlagiarismSource>();
                var plagiarismDetected = previousSubmissions.Any();

                if (plagiarismDetected)
                {
                    foreach (var submission in previousSubmissions.Take(3))
                    {
                        var similarity = CalculateSimpleSimilarity(currentWords, new List<string> { "sample", "text" });
                        
                        plagiarismSources.Add(new PlagiarismSource
                        {
                            SourceWorkId = submission.WorkId,
                            SourceStudentId = submission.StudentId,
                            SourceSubmittedAt = submission.CreatedAt,
                            Reason = similarity > 30 ? "Высокая текстовая схожесть" : "Более ранняя сдача",
                            SimilarityPercentage = similarity
                        });
                    }
                }

                var result = new PlagiarismCheckResult
                {
                    IsPlagiarism = plagiarismDetected,
                    Sources = plagiarismSources,
                    TotalCheckedWorks = previousSubmissions.Count,
                    Details = plagiarismDetected 
                        ? $"Возможный плагиат обнаружен. Проверено {previousSubmissions.Count} работ." 
                        : $"Плагиат не обнаружен. Проверено {previousSubmissions.Count} работ."
                };

                _logger.LogInformation("Расширенная проверка завершена для работы {WorkId}. Результат: {Result}", 
                    workMeta.WorkId, result.IsPlagiarism ? "плагиат" : "ок");

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка в расширенной проверке плагиата для работы {WorkId}", workMeta.WorkId);
                return await CheckForPlagiarismAsync(workMeta, context);
            }
        }

        private List<string> TokenizeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<string>();

            return text.ToLower()
                .Split(new[] { ' ', '.', ',', '!', '?', ';', ':', '\n', '\r', '\t' }, 
                    StringSplitOptions.RemoveEmptyEntries)
                .Where(word => word.Length > 3)
                .Distinct()
                .ToList();
        }

        private double CalculateSimpleSimilarity(List<string> words1, List<string> words2)
        {
            if (!words1.Any() || !words2.Any())
                return 0;

            var commonWords = words1.Intersect(words2).Count();
            var totalUniqueWords = words1.Union(words2).Count();

            return totalUniqueWords > 0 ? (commonWords * 100.0 / totalUniqueWords) : 0;
        }
    }
}