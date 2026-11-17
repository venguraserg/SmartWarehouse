namespace SmartWarehouse.Infrastructure.Pdf;

/// <summary>
/// Логотип комбината для печатных форм: ч/б вариант (tmk_logo_mono_black.svg), выгруженный в PNG 1312×392 —
/// MigraDoc не понимает SVG. Встроен в сборку, как и шрифт.
/// </summary>
internal static class BrandImages
{
    private static readonly Lazy<string> LogoMono = new(() => Load("Images.tmk_logo_mono.png"));

    /// <summary>Логотип ТМК (ч/б) в виде, который принимает MigraDoc: <c>base64:…</c>.</summary>
    public static string LogoMonoBase64 => LogoMono.Value;

    private static string Load(string name)
    {
        using var stream = typeof(BrandImages).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Нет встроенного ресурса {name}.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return "base64:" + Convert.ToBase64String(buffer.ToArray());
    }
}
