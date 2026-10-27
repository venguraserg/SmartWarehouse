# SmartWarehouse

Складской учёт запасных частей и расходных материалов отдела эксплуатации технологического оборудования.

**Стек:** ASP.NET Core Web API (.NET 10), EF Core + PostgreSQL, веб-интерфейс на Blazor. Позже: мобильный клиент.

## Структура

| Проект | Назначение |
|---|---|
| `SmartWarehouse.Domain` | Сущности и правила предметной области. Ни от чего не зависит |
| `SmartWarehouse.Application` | Сервисы (документы и остатки, справочники, места хранения), DTO, интерфейсы репозиториев |
| `SmartWarehouse.Infrastructure` | EF Core, `WarehouseDbContext`, конфигурации таблиц, репозитории, начальное заполнение |
| `SmartWarehouse.API` | REST API, обработка ошибок |
| `SmartWarehouse.Web` | Веб-интерфейс (Blazor, интерактивный серверный режим). Работает только через API |
| `SmartWarehouse.Tests` | Модульные тесты бизнес-правил (xUnit) |

## Модель данных

| Сущность | Таблица | Назначение |
|---|---|---|
| `Category` | `categories` | Категории номенклатуры |
| `UnitOfMeasure` | `units` | Единицы измерения; признак `IsFractional` разрешает дробное количество |
| `Item` | `items` | Номенклатура: артикул, наименование, категория, единица, мин./макс. остаток |
| `ItemImage` | `item_images` | Фото позиции (одно, JPEG/PNG/WebP до 5 МБ), хранится в БД |
| `Warehouse` | `warehouses` | Склад с коротким кодом (`СК1`) |
| `StorageLocation` | `storage_locations` | Дерево мест: зона → стеллаж → полка → ячейка. `FullCode` (`СК1-A-01-03`) — содержимое QR-метки |
| `StockDocument` | `stock_documents` | Документ: приход, выдача, перемещение, списание, корректировка. Номер `ПР-000015`, `ВЫД-000102`... |
| `StockMovement` | `stock_movements` | Строка документа и запись журнала движений |
| `StockBalance` | `stock_balances` | Текущий остаток позиции в месте хранения |
| `Employee` | `employees` | Сотрудники: табельный номер, ФИО, должность, подразделение, телефон, признак «работает». В документах — кто провёл и кому выдано |
| `EmployeeBalance` | `employee_balances` | Подотчёт: сколько позиции числится за сотрудником (выдано, но не списано и не возвращено) |
| `Equipment` | `equipment` | Дерево оборудования: участок → установка → узел |

Единицы `шт`, `компл`, `упак`, `м`, `кг`, `л` создаются автоматически при первом запуске.

## Правила учёта

- Остаток (`StockBalance`) **нельзя изменить напрямую** — только проведением документа.
- Документ проводится **целиком или не проводится вовсе**: если хоть одна строка ошибочна, не меняется ничего.
- Уйти в минус нельзя: это проверяет сервис, дополнительно стоит ограничение в БД.
  От одновременного расхода одного остатка двумя пользователями защищает версия строки
  (`xmin` PostgreSQL); в таком случае API отвечает `409`.
- Товар хранится только в **конечных** местах (без вложенных). В место, где лежит товар, нельзя добавить вложенное.
- Вложенность мест только «вниз»: зона → стеллаж → полка → ячейка (уровни можно пропускать).
- Для штучных единиц количество должно быть целым.

| Тип документа | `fromLocationId` в строке | `toLocationId` в строке |
|---|---|---|
| `Receipt` — приход | — | обязательно |
| `Issue` — выдача сотруднику (`receivedById` обязателен, можно `equipmentId`): товар уходит из ячейки и числится за сотрудником | обязательно | — |
| `Transfer` — перемещение | обязательно | обязательно, другое место |
| `WriteOff` — списание с сотрудника (`receivedById` обязателен), формируется акт PDF | — | — |
| `Return` — возврат от сотрудника на склад (`receivedById` обязателен) | — | обязательно |
| `Adjustment` — корректировка по инвентаризации | одно из двух: `to` увеличивает, `from` уменьшает | |

## API

| Метод | Путь | Что делает |
|---|---|---|
| GET/POST | `/api/categories` | Категории |
| GET/POST | `/api/units` | Единицы измерения |
| GET/POST | `/api/items` | Номенклатура (`?search=`, `?categoryId=`) |
| PUT | `/api/items/{id}` | Изменить позицию |
| GET/PUT/DELETE | `/api/items/{id}/image` | Фото позиции (PUT — multipart, поле `file`) |
| GET/POST | `/api/equipment` | Оборудование |
| GET/POST | `/api/employees` | Сотрудники (`?search=`, `?includeInactive=true`) |
| GET/PUT | `/api/employees/{id}` | Сотрудник |
| GET | `/api/employees/{id}/history` | Подотчёт сотрудника: выдачи (+), списания и возвраты (−) |
| GET | `/api/employees/{id}/custody` | Что числится за сотрудником |
| GET | `/api/custody` | Подотчёт всех сотрудников (`employeeId`, `itemId`, `search`) |
| GET | `/api/documents/{id}/act` | Акт на списание (PDF) |
| GET | `/api/items/{id}/balances` | Остатки позиции по местам |
| GET | `/api/items/{id}/movements` | История движений с номерами документов |
| GET/POST | `/api/warehouses` | Склады |
| GET | `/api/warehouses/{id}/locations` | Дерево мест хранения |
| POST | `/api/warehouses/{id}/locations` | Добавить место (`parentId`, `type`, `code`) |
| GET | `/api/locations/by-code/{fullCode}` | Найти место по QR-коду |
| GET | `/api/locations/{id}/content` | Что лежит в месте |
| GET/POST | `/api/documents` | Список документов (`?type=`) / создать и провести документ |
| GET | `/api/documents/{id}` | Документ со строками |
| GET | `/api/stock` | Что лежит по факту: `warehouseId`, `location` (код места — с вложенными; можно без кода склада), `search`, `categoryId`, `belowMinimum` |
| GET | `/api/stock/below-minimum` | Позиции ниже минимума и сколько пополнить |

Примеры запросов — в `SmartWarehouse.API/SmartWarehouse.API.http`.

## Запуск

### 1. Миграция БД

Если папки `SmartWarehouse.Infrastructure/Data/Migrations` ещё нет — создайте её и закоммитьте:

```bash
dotnet tool install --global dotnet-ef
dotnet ef migrations add InitialCreate -p SmartWarehouse.Infrastructure -s SmartWarehouse.API -o Data/Migrations
```

После изменения модели добавляйте новую миграцию с понятным именем, например `dotnet ef migrations add AddSuppliers ...`.

### 2. Локально (Visual Studio)

Строка подключения в репозитории не хранится. Правой кнопкой по `SmartWarehouse.API` →
**Manage User Secrets**, и впишите:

```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Port=5432;Database=smartwarehouse;Username=postgres;Password=<пароль>"
  },
  "Database": { "MigrateOnStartup": true }
}
```

Запуск API и веб-интерфейса вместе: правой кнопкой по решению → **Configure Startup Projects** →
**Multiple startup projects** → для `SmartWarehouse.API` и `SmartWarehouse.Web` выберите **Start** → F5.

- API: http://localhost:5113
- Веб-интерфейс: http://localhost:5200 (адрес API задан в `SmartWarehouse.Web/appsettings.json`, `Api:BaseUrl`)

При первом запуске база и таблицы создадутся автоматически, справочник единиц заполнится.

### 3. Docker (сервер)

```bash
cp .env.example .env      # задайте POSTGRES_PASSWORD
docker compose up -d --build
```

Веб-интерфейс — на порту `WEB_PORT` (по умолчанию 8081), API — на `API_PORT` (8080). Миграции применяются при старте.

## Тесты

```bash
dotnet test
```

При каждом пуше и pull request GitHub Actions собирает решение, прогоняет тесты и собирает Docker-образы API и веб-интерфейса
(см. `.github/workflows/ci.yml`).
