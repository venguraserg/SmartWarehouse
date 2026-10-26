using SmartWarehouse.Domain.Exceptions;

namespace SmartWarehouse.Application.Services;

/// <summary>Проверка загружаемых изображений: размер и формат по «магическим байтам», а не по имени файла.</summary>
public static class ImageRules
{
    public const int MaxBytes = 5 * 1024 * 1024;

    public static string DetectContentType(byte[] data)
    {
        if (data.Length == 0)
            throw new DomainException("Файл изображения пустой.");
        if (data.Length > MaxBytes)
            throw new DomainException($"Изображение больше {MaxBytes / 1024 / 1024} МБ.");

        if (data is [0xFF, 0xD8, 0xFF, ..])
            return "image/jpeg";

        if (data is [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..])
            return "image/png";

        // RIFF....WEBP
        if (data.Length >= 12 && data is [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..])
            return "image/webp";

        throw new DomainException("Поддерживаются только изображения JPEG, PNG и WebP.");
    }
}
