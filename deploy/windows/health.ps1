<#
.SYNOPSIS
    Общие функции скриптов обновления и отката: подключение к базе из настроек API, проверка служб, восстановление программы.
    Подключается в других скриптах:  . (Join-Path $PSScriptRoot "health.ps1")
#>

function Get-DbConnection([string]$apiDir) {
    $config = Get-Content (Join-Path $apiDir "appsettings.Production.json") -Raw -Encoding UTF8 | ConvertFrom-Json
    $parts = @{}
    foreach ($pair in $config.ConnectionStrings.Default.Split(';')) {
        $kv = $pair.Split('=', 2)
        if ($kv.Count -eq 2) { $parts[$kv[0].Trim().ToLowerInvariant()] = $kv[1].Trim() }
    }
    [pscustomobject]@{
        Host     = $parts["host"]
        Port     = $(if ($parts["port"]) { $parts["port"] } else { "5432" })
        User     = $parts["username"]
        Password = $parts["password"]
        Database = $parts["database"]
    }
}

function Get-PgTool([string]$name) {
    $tool = Get-ChildItem "C:\Program Files\PostgreSQL\*\bin\$name.exe" | Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $tool) { throw "$name не найден (PostgreSQL установлен в другую папку?)." }
    $tool.FullName
}

# Адрес проверки службы из её настроек (Urls): http://localhost:<порт>/health.
function Get-HealthUrl([string]$dir) {
    $config = Get-Content (Join-Path $dir "appsettings.Production.json") -Raw -Encoding UTF8 | ConvertFrom-Json
    $url = ($config.Urls -split ';')[0]
    $port = if ($url -match ':(\d+)/?$') { $Matches[1] } else { "80" }
    "http://localhost:$port/health"
}

# Ждёт ответа 200 от адреса проверки. $AnyAnswer — достаточно любого ответа сервера (прежние версии без /health).
function Wait-Health([string]$url, [int]$seconds, [switch]$AnyAnswer) {
    $deadline = (Get-Date).AddSeconds($seconds)
    $last = ""
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 5
            if ($response.StatusCode -eq 200) { Write-Host "    $url — ok" -ForegroundColor Green; return $true }
            $last = "код $($response.StatusCode)"
        }
        catch {
            $status = $_.Exception.Response.StatusCode.value__
            if ($AnyAnswer -and $status -and $status -lt 500) { Write-Host "    $url — отвечает" -ForegroundColor Green; return $true }
            $last = if ($status) { "код $status" } else { $_.Exception.Message }
        }
        Start-Sleep -Seconds 3
    }
    Write-Host "    $url — нет ответа за $seconds с ($last)" -ForegroundColor Red
    return $false
}

# Возвращает программу (api, web) из папки previous. Службы должны быть остановлены.
function Restore-Program([string]$previousDir, [string]$apiDir, [string]$webDir) {
    if (-not (Test-Path (Join-Path $previousDir "api"))) { throw "Нет прежней версии в $previousDir — откатывать не из чего." }
    foreach ($pair in @(@("api", $apiDir), @("web", $webDir))) {
        if (Test-Path $pair[1]) { Remove-Item $pair[1] -Recurse -Force }
        Copy-Item (Join-Path $previousDir $pair[0]) $pair[1] -Recurse
    }
}
