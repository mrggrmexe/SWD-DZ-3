using System.Text.RegularExpressions;

namespace FileStoringService.Helpers;

public static class WorkIdHelper
{
    private static readonly Regex HexRegex = new Regex("^[0-9A-Fa-f]{8}$", RegexOptions.Compiled);
    private static readonly Regex GuidRegex = new Regex(
        "^[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}$", 
        RegexOptions.Compiled);

    /// <summary>
    /// Проверяет, является ли строка валидным WorkId (Guid или hex-число)
    /// </summary>
    public static bool IsValidWorkId(string workId)
    {
        if (string.IsNullOrWhiteSpace(workId))
            return false;

        return GuidRegex.IsMatch(workId) || HexRegex.IsMatch(workId);
    }

    /// <summary>
    /// Нормализует WorkId: преобразует hex в Guid формат
    /// </summary>
    public static string NormalizeWorkId(string workId)
    {
        if (string.IsNullOrWhiteSpace(workId))
            return workId;

        // Если это Guid, возвращаем как есть
        if (GuidRegex.IsMatch(workId))
            return workId.ToLower();

        // Если это hex (8 символов), преобразуем в Guid
        if (HexRegex.IsMatch(workId))
        {
            try
            {
                // Преобразуем hex в Guid (первые 8 символов)
                var hexString = workId.PadRight(32, '0');
                var guid = Guid.Parse(hexString);
                return guid.ToString().ToLower();
            }
            catch
            {
                // Если не удалось преобразовать, возвращаем исходное значение
                return workId;
            }
        }

        return workId;
    }

    /// <summary>
    /// Конвертирует Work в числовой ID для клиента
    /// </summary>
    public static int GetClientWorkId(string workId)
    {
        if (string.IsNullOrWhiteSpace(workId))
            return 0;

        try
        {
            // Берем первые 8 символов Guid без дефисов
            var cleanId = workId.Replace("-", "").Substring(0, 8);
            return Convert.ToInt32(cleanId, 16);
        }
        catch
        {
            // Fallback: используем хэш
            return Math.Abs(workId.GetHashCode() % 100000000);
        }
    }
}