# SmartWarehouse

Складской учёт запасных частей и расходных материалов отдела эксплуатации технологического оборудования.

**Стек:** ASP.NET Core Web API (.NET 10), EF Core + PostgreSQL. Позже: Blazor (веб) и мобильный клиент.

## Структура

| Проект | Назначение |
|---|---|
| `SmartWarehouse.Domain` | Сущности и правила предметной области. Ни от чего не зависит |
| `SmartWarehouse.Application` | Сервисы (учёт движений, справочники), DTO, интерфейсы репозиториев |
| `SmartWarehouse.Infrastructure` | EF Core, `WarehouseDbContext`, конфигурации таблиц, репозитории |
| `SmartWarehouse.API` | REST API, обработка ошибок |
| `SmartWarehouse.Tests` | Модульные тесты бизнес-правил (xUnit) |

## Главное правило учёта

Остаток (`StockBalance`) **нельзя изменить напрямую**. Он меняется только вместе с записью
в журнале движений (`StockMovement`) внутри одной транзакции. Уйти в минус нельзя:
это проверяет сервис, дополнительно стоит ограничение в БД. От одновременной выдачи
одного и того же остатка двумя пользователями защищает версия строки (`xmin` PostgreSQL),
и в таком случае API отвечает `409`.

| Операция | `fromLocationId` | `toLocationId` |
|---|---|---|
| `Receipt` — приход | — | обязательно |
| `Issue` — выдача (можно указать `equipmentId`) | обязательно | — |
| `Transfer` — перемещение | обязательно | обязательно |
| `WriteOff` — списание | обязательно | — |
| `Adjustment` — корректировка по инвентаризации | одно из двух: `to` увеличивает, `from` уменьшает | |

## API

| Метод | Путь | Что делает |
|---|---|---|
| GET/POST | `/api/items` | Номенклатура (поиск `?search=`) |
| GET | `/api/items/{id}/balances` | Остатки позиции по местам |
| GET | `/api/items/{id}/movements` | История движений |
| GET/POST | `/api/warehouses` | Склады с местами хранения |
| POST | `/api/warehouses/{id}/locations` | Добавить ячейку |
| POST | `/api/stock/movements` | Складская операция |
| GET | `/api/stock/below-minimum` | Позиции ниже минимума и сколько дозаказать |

Примеры запросов лежат в `SmartWarehouse.API/SmartWarehouse.API.http`.

## Запуск

### Первый раз: создать миграцию БД

```bash
dotnet tool install --global dotnet-ef
dotnet ef migrations add InitialCreate -p SmartWarehouse.Infrastructure -s SmartWarehouse.API -o Data/Migrations
```

Созданную папку `SmartWarehouse.Infrastructure/Data/Migrations` нужно закоммитить.

### Локально (Visual Studio / dotnet run)

Строка подключения в репозитории не хранится. Задайте её через user-secrets:

```bash
cd SmartWarehouse.API
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Database=smartwarehouse;Username=postgres;Password=<пароль>"
dotnet ef database update -p ../SmartWarehouse.Infrastructure
dotnet run
```

### Docker (сервер)

```bash
cp .env.example .env      # задайте POSTGRES_PASSWORD
docker compose up -d --build
```

API будет доступен на порту из `API_PORT` (по умолчанию 8080). Миграции применяются при старте.

## Тесты

```bash
dotnet test
```

При каждом пуше и pull request GitHub Actions собирает решение, прогоняет тесты и собирает Docker-образ
(см. `.github/workflows/ci.yml`).
