# Сервер на Windows (без Docker)

Программа работает на отдельном ПК как **две службы Windows** (API и веб-интерфейс) + PostgreSQL.
Службы запускаются сами при включении ПК — входить в Windows не нужно.

| Что | Где |
|---|---|
| Сайт для пользователей | `http://<IP сервера>` (порт 80) |
| Программа | `C:\SmartWarehouse\api`, `C:\SmartWarehouse\web` |
| Настройки (пароль БД, ключ входа) | `C:\SmartWarehouse\api\appsettings.Production.json`, `...\web\appsettings.Production.json` |
| Резервные копии базы (каждый день в 02:00, 30 дней) | `C:\SmartWarehouse\backups` |
| Журнал копирования | `C:\SmartWarehouse\logs\backup.log` |
| Журнал программы | Просмотр событий → Журналы Windows → Приложение |

## 1. Подготовка ПК
- Windows 10/11 или Windows Server, все обновления установлены.
- **Постоянный IP** от IT (или зарезервированный в DHCP). Он попадает в QR-коды наклеек — менять его потом неудобно.
- Электропитание: «Спящий режим — никогда», отключить «Разрешить отключение устройства для экономии энергии» у сетевой карты.
- Не выключать ПК на ночь (резервная копия делается в 02:00; если ПК был выключен — сделается при включении).

## 2. Установить программы (один раз)
1. **.NET 10 SDK** — https://dotnet.microsoft.com/download/dotnet/10.0 → SDK → Windows x64.
2. **Git** — https://git-scm.com/download/win (все настройки по умолчанию).
3. **PostgreSQL 17** — https://www.postgresql.org/download/windows/ → установщик EDB. Запомните **пароль postgres**;
   порт 5432; «Stack Builder» в конце можно не запускать.

## 3. Получить код
PowerShell **от имени администратора**:
```powershell
cd C:\
git clone https://github.com/venguraserg/SmartWarehouse.git C:\SmartWarehouse-src
cd C:\SmartWarehouse-src
```

## 4. Установить
```powershell
powershell -ExecutionPolicy Bypass -File deploy\windows\install.ps1 -ServerAddress 192.168.1.10
```
(вместо `192.168.1.10` — IP этого ПК). Скрипт спросит пароль postgres, соберёт программу, создаст службы,
откроет порт 80 в брандмауэре и включит ежедневную резервную копию. В конце напишет адрес сайта.

Первый вход: `admin` / `admin123` — сразу смените пароль.

Если порт 80 занят (например, IIS) — добавьте `-WebPort 8081`, сайт будет `http://<IP>:8081`.

## Обновление
Когда в `main` появились изменения (после мержа PR):
```powershell
cd C:\SmartWarehouse-src
powershell -ExecutionPolicy Bypass -File deploy\windows\update.ps1
```
Делается резервная копия, подтягивается код, программа пересобирается, службы перезапускаются.
Изменения базы применяются автоматически при запуске API.

## Резервные копии
- Вручную: `powershell -ExecutionPolicy Bypass -File C:\SmartWarehouse\scripts\backup.ps1`
- **Копируйте папку `backups` на другой компьютер или сетевой диск** — копия на том же диске не спасёт при его поломке.
- Восстановление — см. комментарий в начале `backup.ps1`.

## Управление
- Службы: `services.msc` → «SmartWarehouse — API склада», «SmartWarehouse — веб-интерфейс склада».
- Перезапуск: `Restart-Service SmartWarehouse.Api, SmartWarehouse.Web`
- Удаление служб (данные остаются): `deploy\windows\uninstall.ps1`

## Что-то не работает
| Признак | Что проверить |
|---|---|
| С другого ПК сайт не открывается, на сервере `http://localhost` работает | Брандмауэр (правило «SmartWarehouse Web»), антивирус, тот ли IP; может, сеть помечена как «Общедоступная» и её блокирует политика IT |
| Сайт не открывается и на сервере | `services.msc` — запущены ли службы; Просмотр событий → Приложение — ошибки SmartWarehouse.* |
| «Сервер склада (API) недоступен» | Служба API не запущена или не подключилась к PostgreSQL (пароль в `api\appsettings.Production.json`) |
| После переустановки всех выкинуло из входа | Нормально, если удаляли `C:\SmartWarehouse\keys` — войти заново |
