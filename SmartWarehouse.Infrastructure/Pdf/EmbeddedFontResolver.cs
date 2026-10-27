using System.Reflection;
using PdfSharp.Fonts;

namespace SmartWarehouse.Infrastructure.Pdf;

/// <summary>
/// Шрифты для PDF берутся из сборки (DejaVu Sans, с кириллицей), а не из системы:
/// на сервере в Docker системных шрифтов может не быть.
/// </summary>
internal sealed class EmbeddedFontResolver : IFontResolver
{
    public const string Family = "DejaVu Sans";

    private const string Regular = "DejaVuSans";
    private const string Bold = "DejaVuSans-Bold";

    private static readonly object Sync = new();

    /// <summary>Регистрирует резолвер один раз на процесс.</summary>
    public static void EnsureRegistered()
    {
        lock (Sync)
        {
            if (GlobalFontSettings.FontResolver is not EmbeddedFontResolver)
                GlobalFontSettings.FontResolver = new EmbeddedFontResolver();
        }
    }

    // Курсив не встроен — используется прямое начертание.
    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
        new(bold ? Bold : Regular);

    public byte[]? GetFont(string faceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Fonts.{faceName}.ttf")
            ?? throw new InvalidOperationException($"Шрифт {faceName} не найден в ресурсах сборки.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
