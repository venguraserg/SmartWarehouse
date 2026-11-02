<#
.SYNOPSIS
    Удаление служб SmartWarehouse, задания резервного копирования и правила брандмауэра.
    Папка C:\SmartWarehouse (настройки, резервные копии) и база данных НЕ удаляются.
#>
param([int]$WebPort = 80)

$ErrorActionPreference = "Continue"
foreach ($name in "SmartWarehouse.Web", "SmartWarehouse.Api") {
    if (Get-Service -Name $name -ErrorAction SilentlyContinue) {
        Stop-Service $name -Force
        & sc.exe delete $name | Out-Null
        Write-Host "Удалена служба $name"
    }
}
Unregister-ScheduledTask -TaskName "SmartWarehouse Backup" -Confirm:$false -ErrorAction SilentlyContinue
Remove-NetFirewallRule -DisplayName "SmartWarehouse Web ($WebPort)" -ErrorAction SilentlyContinue
Write-Host "Готово. Данные и настройки остались в C:\SmartWarehouse и в PostgreSQL."
