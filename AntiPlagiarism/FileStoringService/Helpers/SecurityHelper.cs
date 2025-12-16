using System.Security;
using System.Text.RegularExpressions;

namespace FileStoringService.Helpers;

public static class SecurityHelper
{
    private static readonly Regex DangerousPathRegex = new Regex(
        @"(\.\.(\\|/))|(%2e%2e)|(%252e%252e)|(\x5c\x5c)|(\x2f\x2f)|(\x3a\x3a)", 
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Проверяет, что путь безопасен и находится внутри разрешенной директории
    /// </summary>
    public static bool IsPathSafe(string filePath, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        try
        {
            // Проверка на опасные последовательности
            if (DangerousPathRegex.IsMatch(filePath))
            {
                return false;
            }

            // Получаем канонические пути
            var fullPath = Path.GetFullPath(filePath);
            var baseFullPath = Path.GetFullPath(baseDirectory);

            // Убеждаемся, что путь не выходит за пределы базовой директории
            return fullPath.StartsWith(baseFullPath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            // Если произошла ошибка при обработке пути, считаем его небезопасным
            return false;
        }
    }

    /// <summary>
    /// Проверяет, что имя файла безопасно
    /// </summary>
    public static bool IsSafeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        // Запрещенные символы в именах файлов
        var invalidChars = Path.GetInvalidFileNameChars();
        if (fileName.Any(c => invalidChars.Contains(c)))
        {
            return false;
        }

        // Запрещенные имена (Windows)
        var reservedNames = new[]
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        if (reservedNames.Contains(nameWithoutExtension.ToUpperInvariant()))
        {
            return false;
        }

        // Проверка на чрезмерно длинные имена
        if (fileName.Length > 255)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Создает безопасное имя файла
    /// </summary>
    public static string GetSafeFileName(string originalFileName, string workId)
    {
        if (string.IsNullOrWhiteSpace(originalFileName))
            return $"{workId}_file.bin";

        var extension = Path.GetExtension(originalFileName);
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(originalFileName);

        // Убираем опасные символы
        var invalidChars = Path.GetInvalidFileNameChars();
        var safeName = new string(fileNameWithoutExtension
            .Where(c => !invalidChars.Contains(c))
            .ToArray());

        // Обрезаем если слишком длинное
        if (safeName.Length > 100)
        {
            safeName = safeName.Substring(0, 100);
        }

        // Если имя стало пустым, используем workId
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = workId;
        }

        return $"{workId}_{safeName}{extension}";
    }
}