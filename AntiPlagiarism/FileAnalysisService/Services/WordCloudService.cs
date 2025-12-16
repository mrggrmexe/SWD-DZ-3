using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FileAnalysisService.Services
{
    /// <summary>
    /// Сервис для генерации облака слов через QuickChart API
    /// </summary>
    public interface IWordCloudService
    {
        /// <summary>
        /// Генерирует облако слов из текста
        /// </summary>
        /// <param name="text">Текст для анализа</param>
        /// <returns>URL сгенерированного изображения</returns>
        Task<string?> GenerateWordCloudAsync(string text);

        /// <summary>
        /// Генерирует облако слов с кастомными настройками
        /// </summary>
        Task<string?> GenerateWordCloudAsync(string text, WordCloudOptions options);
    }

    public class WordCloudService : IWordCloudService
    {
        private readonly ILogger<WordCloudService> _logger;
        private readonly HttpClient _httpClient;
        private readonly WordCloudSettings _settings;
        private static readonly JsonSerializerOptions _jsonOptions = new() 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
        };

        public WordCloudService(
            ILogger<WordCloudService> logger,
            IHttpClientFactory httpClientFactory,
            IOptions<WordCloudSettings> settings)
        {
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient("WordCloud");
            _settings = settings.Value;
        }

        public async Task<string?> GenerateWordCloudAsync(string text)
        {
            var options = new WordCloudOptions
            {
                Width = _settings.DefaultWidth,
                Height = _settings.DefaultHeight,
                MaxWords = _settings.MaxWords,
                Colors = _settings.Colors?.ToList() ?? new List<string> { "#375E97", "#FB6542", "#FFBB00", "#3F681C" }
            };

            return await GenerateWordCloudAsync(text, options);
        }

        public async Task<string?> GenerateWordCloudAsync(string text, WordCloudOptions options)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogWarning("Пустой текст для генерации Word Cloud");
                return null;
            }

            try
            {
                _logger.LogInformation("Генерация Word Cloud для текста длиной {Length} симвонов", text.Length);

                // Очищаем текст от лишних символов и ограничиваем длину
                var cleanedText = CleanText(text);
                
                if (string.IsNullOrWhiteSpace(cleanedText))
                {
                    _logger.LogWarning("Текст после очистки пуст");
                    return _settings.FallbackImageUrl;
                }

                // Создаем запрос к QuickChart API
                var request = new
                {
                    format = "png",
                    width = options.Width,
                    height = options.Height,
                    chart = new
                    {
                        type = "wordCloud",
                        data = new
                        {
                            text = cleanedText
                        },
                        options = new
                        {
                            layout = new[] { "word", "count" },
                            maxWords = options.MaxWords,
                            minWordLength = 3,
                            colors = options.Colors,
                            fontFamily = options.FontFamily,
                            backgroundColor = options.BackgroundColor,
                            scale = options.Scale,
                            rotation = options.Rotation,
                            padding = options.Padding
                        }
                    }
                };

                var jsonContent = JsonSerializer.Serialize(request, _jsonOptions);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                // Отправляем запрос с таймаутом
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var response = await _httpClient.PostAsync(_settings.ApiUrl, content, cts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Ошибка QuickChart API: {StatusCode}, {Error}", 
                        response.StatusCode, errorContent);
                    
                    // Пробуем использовать статический URL как fallback
                    return GenerateStaticUrl(cleanedText, options);
                }

                var responseData = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<QuickChartResponse>(responseData, _jsonOptions);

                if (string.IsNullOrEmpty(result?.Url))
                {
                    _logger.LogWarning("Пустой URL в ответе QuickChart");
                    return GenerateStaticUrl(cleanedText, options);
                }

                _logger.LogInformation("Word Cloud успешно сгенерирован: {Url}", result.Url);
                return result.Url;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Ошибка сети при генерации Word Cloud");
                return _settings.FallbackImageUrl;
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning("Таймаут при генерации Word Cloud");
                return _settings.FallbackImageUrl;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Неожиданная ошибка при генерации Word Cloud");
                return _settings.FallbackImageUrl;
            }
        }

        /// <summary>
        /// Очистка текста для Word Cloud
        /// </summary>
        private string CleanText(string text)
        {
            // Удаляем HTML-теги
            var noHtml = System.Text.RegularExpressions.Regex.Replace(text, "<.*?>", string.Empty);
            
            // Удаляем спецсимволы, оставляем буквы, цифры и пробелы
            var cleaned = System.Text.RegularExpressions.Regex.Replace(noHtml, @"[^\w\s]", " ");
            
            // Удаляем лишние пробелы
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", " ");
            
            // Приводим к нижнему регистру
            cleaned = cleaned.ToLowerInvariant();
            
            // Удаляем стоп-слова
            var stopWords = new HashSet<string> 
            { 
                "the", "and", "or", "but", "is", "are", "was", "were", "be", "been", 
                "have", "has", "had", "do", "does", "did", "will", "would", "should", 
                "could", "can", "may", "might", "must", "a", "an", "the", "in", "on", 
                "at", "to", "for", "of", "with", "by", "from", "as", "that", "this",
                "these", "those", "it", "its", "they", "them", "their", "what", "which"
            };
            
            var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(word => word.Length > 2 && !stopWords.Contains(word))
                .Take(200); // Ограничиваем количество слов

            return string.Join(" ", words);
        }

        /// <summary>
        /// Генерация статического URL (fallback)
        /// </summary>
        private string GenerateStaticUrl(string text, WordCloudOptions options)
        {
            var encodedText = HttpUtility.UrlEncode(text);
            return $"https://quickchart.io/wordcloud?text={encodedText}&width={options.Width}&height={options.Height}";
        }
    }

    /// <summary>
    /// Настройки Word Cloud
    /// </summary>
    public class WordCloudOptions
    {
        public int Width { get; set; } = 800;
        public int Height { get; set; } = 600;
        public int MaxWords { get; set; } = 100;
        public List<string> Colors { get; set; } = new();
        public string FontFamily { get; set; } = "Arial";
        public string BackgroundColor { get; set; } = "#ffffff";
        public string Scale { get; set; } = "linear";
        public object Rotation { get; set; } = new { from = 0, to = 0, numOfOrientation = 1 };
        public int Padding { get; set; } = 1;
    }

    /// <summary>
    /// Ответ QuickChart API
    /// </summary>
    public class QuickChartResponse
    {
        public string? Url { get; set; }
        public string? Error { get; set; }
    }

    /// <summary>
    /// Конфигурация Word Cloud
    /// </summary>
    public class WordCloudSettings
    {
        public string ApiUrl { get; set; } = "https://quickchart.io/wordcloud";
        public int DefaultWidth { get; set; } = 800;
        public int DefaultHeight { get; set; } = 600;
        public int MaxWords { get; set; } = 100;
        public string[]? Colors { get; set; }
        public string FallbackImageUrl { get; set; } = "https://quickchart.io/chart?c={type:'wordCloud'}";
    }
}