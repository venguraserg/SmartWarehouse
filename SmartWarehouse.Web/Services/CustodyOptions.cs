namespace SmartWarehouse.Web.Services;

/// <summary>Настройки подотчёта (раздел Custody в appsettings.json).</summary>
public class CustodyOptions
{
    /// <summary>Через сколько дней выданное, но не списанное и не возвращённое, подсвечивается как давнее.</summary>
    public int AgeWarningDays { get; set; } = 30;
}
