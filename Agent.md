# Agent.md — шпаргалка по репозиторию SmartWarehouse

Здесь собрано главное для ИИ-агентов и новых разработчиков. Подробности для пользователей лежат в `README.md`,
про сервер на Windows — в `deploy/windows/README.md`, про Android — в `mobile/android/README.md`.

## 1. Что это

Складской учёт запасных частей и расходников для отдела эксплуатации технологического оборудования (бренд ТМК в
печатных формах и иконках). Главная учётная система предприятия — **1С**. Это приложение рассчитано на персонал склада:
приход по накладным 1С, размещение по ячейкам, выдача сотрудникам в подотчёт, акты на списание, инвентаризация,
сверка с 1С, QR-наклейки, работа со сканерами (USB/Bluetooth, камера, ТСД).

**Язык проекта — русский**: UI, сообщения об ошибках, XML-комментарии, коммиты, README. Новый код пишите в том же стиле.

## 2. Стек

| Слой | Технологии |
|---|---|
| Backend | ASP.NET Core Web API, **.NET 10** (`net10.0`), Nullable + ImplicitUsings включены |
| БД | **PostgreSQL 17**, EF Core 10 + Npgsql. Оптимистичная блокировка через `xmin` |
| Frontend | **Blazor Web App, Interactive Server** (`SmartWarehouse.Web`). Работает **только через REST API**, к БД не обращается |
| Auth | JWT Bearer в API; в Web — зашифрованная cookie, в ней лежит токен API. Пароли хешируются PBKDF2 (`Microsoft.Extensions.Identity.Core`) |
| PDF | PDFsharp-MigraDoc 6.1.1, шрифт DejaVu встроен в сборку (`Pdf/Fonts/*.ttf`), QR — QRCoder |
| Excel | ClosedXML (`.xlsx`), ExcelDataReader (`.xls` из 1С) |
| Тесты | xUnit 2.9 + GitHubActionsTestLogger. Тесты на реальном PostgreSQL включаются переменной `SW_TEST_DB` |
| Mobile | `mobile/android`: Kotlin-оболочка с WebView (Gradle 8.11.1, JDK 17), сканер Google Play Services, Intent-сканеры ТСД |
| Инструменты | `tools/DemoSeed` (демо-данные, C#), `tools/ui-snapshots` (Playwright, Node 20), `tools/opening` (Python + openpyxl: файл начальных остатков из 1С) |
| Деплой | Docker Compose (Linux), Caddy для HTTPS (`docker-compose.https.yml`, `deploy/Caddyfile`), службы Windows (`deploy/windows/*.ps1`) |

Решение — файл **`SmartWarehouse.slnx`** (новый XML-формат).

## 3. Архитектура (Clean Architecture)

```
Domain  ←  Application  ←  Infrastructure  ←  API
                ↑
               Web (ссылается только на Application ради DTO и enum'ов)
Tests → Application, Infrastructure, Web
```

| Проект | Содержимое |
|---|---|
| `SmartWarehouse.Domain` | `Entities/` (Item, Batch, StockBalance, StockDocument, StockMovement, Employee, EmployeeBalance, WriteOffAct, IssueRequest, InventoryCount, ItemNote, UserAccount, Warehouse/StorageLocation, Equipment, AppSetting, Reference — категории/единицы), `Enums/` (MovementType, UserRole [Flags], ActStatus, ActKind, BatchOrigin, CustodyKind, InventoryStatus, IssueRequestStatus, LocationType, NoteKind), `Exceptions/DomainExceptions.cs`. Ни от чего не зависит |
| `SmartWarehouse.Application` | `Services/` с бизнес-логикой (StockService — ядро учёта, WriteOffActService, ActService, ReceivingService, ReconcileService, OpeningImportService, InventoryService, IssueRequestService, CatalogService, LocationService, EmployeeService, LabelService, ScanService, SettingsService, MolService, AccountService и др.), вспомогательные правила (CodeRules, Transliteration, KeyboardLayout, LocationCodeMatch, SearchWords, UnitAliases, Valuation). `DTOs/` (в основном `Dtos.cs`, это record'ы), `Interfaces/` (Repositories.cs, Services.cs, Accounts.cs, Import.cs, ReferenceAdmin.cs). Регистрация сервисов — `DependencyInjection.AddApplication()` |
| `SmartWarehouse.Infrastructure` | `Data/WarehouseDbContext.cs`, `Configurations.cs` (Fluent API, имена таблиц в snake_case), `DatabaseSetup.cs` (миграции, в том числе для старых баз без миграций), `DbSeeder.cs`, `LatinCodesSql.cs`, `Migrations/`. `Repositories/` реализуют интерфейсы из Application, плюс `Pdf/` (ActPdfRenderer, LabelPdfRenderer), `Import/ExcelImportReader.cs`, `Security/IdentityPasswordHasher.cs`. Регистрация — `AddInfrastructure(connectionString)` |
| `SmartWarehouse.API` | Тонкие контроллеры в `Controllers/`: primary constructor, `[ApiController]`, `[Route("api/...")]`, вызов сервиса напрямую. `Security/Auth.cs` содержит роли, JWT, `TokenService` и расширения `ClaimsPrincipal`. `Security/PriceMaskingFilter.cs` скрывает цены от рядовых сотрудников. `Infrastructure/ApiExceptionHandler.cs` переводит исключения в ProblemDetails. Примеры запросов — `SmartWarehouse.API.http` |
| `SmartWarehouse.Web` | `Components/Pages/*.razor` (~44 страницы: Stock, Items, ItemCard, DocumentNew, Receiving, Custody, Acts, Reports, SummaryActs, IssueRequests, Inventories, Reconcile, Labels, Scan, Admin* …), `Components/Shared/*.razor` (~27 компонентов: PlacePicker, TreePicker, CodeInput, Quick* для создания справочников на ходу и др.), `Services/ApiClient.cs` (единственный типизированный клиент API: добавляет Bearer, по 401 отправляет на вход), ScannerHub, RequestCart, BrowserStore, NavMenu, UiThemes. В `wwwroot/js` лежат scanner.js, keyboard-scanner.js, latin-input.js, android-app.js, theme.js |
| `SmartWarehouse.Tests` | Тесты сервисов на фейковых репозиториях (`FakeStockRepository.cs`, `FixedTimeProvider`), Excel-фикстуры в `Fixtures/`, `Postgres/` — интеграционные тесты на настоящей БД |

## 4. Ключевые бизнес-правила (не нарушать!)

- Остаток (`StockBalance`) **меняется только проведением документа** (`StockService.PostDocumentAsync`), напрямую — никогда.
- Документ проводится **целиком или не проводится вовсе**. Уходить в минус нельзя: это проверяет сервис, и в БД стоит отдельное ограничение.
  При гонке EF выбрасывает `DbUpdateConcurrencyException` (`xmin`), API отвечает **409**.
- В одном документе не больше **500 строк** (`StockService.MaxLines`).
- **Партии (`Batch`)**: каждая строка прихода — своя партия с ценой. Партия переходит вместе с товаром: ячейка → сотрудник → акт.
  По умолчанию берётся самая старая (FIFO). Признак `OffBook` («вне учёта») означает, что в 1С такой партии нет и МОЛ у неё не ведётся.
- Типы движений (`MovementType`): Receipt, Issue, Transfer, WriteOff, Adjustment, Return, CustodyChange, MolChange.
  Через `PostDocumentAsync` **нельзя** провести `WriteOff` (только через утверждение акта на списание), `CustodyChange` и `MolChange` (только из сверки с 1С).
- Товар лежит только в **конечных** местах хранения. Иерархия идёт сверху вниз: зона → стеллаж → полка → ячейка.
- Для штучных единиц (`UnitOfMeasure.IsFractional = false`) количество должно быть целым.
- **Коды только латиницей**: склад и место (`WH1-A-01-03`), артикул (`SP-000012`, `BRG-000001`), штрих-код. Кириллицу из
  русской раскладки переводит обратно `KeyboardLayout` / `latin-input.js`.
- Жизненный цикл акта на списание: Черновик → На утверждении (позиции зарезервированы) → Утверждён (проводится документ `СП-…`) или Возвращён.
  Утверждает непосредственный руководитель. Если руководителя нет, акт утверждается сразу при отправке.
- Повторно принять ту же накладную 1С (номер + дата) нельзя. Импорт идёт в место «Приёмка» `WH1-RCV`, затем товар размещают перемещением.
- Номера документов: `ПР-000015`, `ВЫД-000102`, `СП-…`.

## 5. Роли и доступ

`UserRole` — это `[Flags]`: Employee=1, Manager=2, Storekeeper=4, Admin=8, ShiftLead=16. В API используются константы из
`SmartWarehouse.API.Security.Roles`: `Roles.Warehouse` (Storekeeper+Admin) и `Roles.Issuing` (Warehouse+ShiftLead).

- Fallback-политика API: **все эндпоинты требуют аутентификации**, исключения помечены `[AllowAnonymous]` (login, `/health`, OpenAPI в dev).
- Цены видят Manager, Storekeeper и Admin (`SeesPrices()`). Остальным `PriceMaskingFilter` их обнуляет.
- Весь подотчёт видят Storekeeper, Admin и ShiftLead (`SeesAllCustody()`).
- Первый запуск: пользователь `admin` / `admin123` (или `Auth:InitialAdminPassword`), при первом входе система требует сменить пароль.

## 6. Обработка ошибок

Бизнес-ошибки выбрасываются как исключения из `SmartWarehouse.Domain.Exceptions`, с **понятным пользователю русским текстом**:

| Исключение | HTTP |
|---|---|
| `AuthenticationFailedException` | 401 |
| `ForbiddenException` | 403 |
| `NotFoundException` | 404 |
| `InsufficientStockException` | 409 |
| `DomainException` (база для остальных) | 400 |
| `DbUpdateConcurrencyException` / `DbUpdateException` | 409 |

На стороне Web `ApiClient` превращает ответы в `ApiException(message, status)`, а страницы показывают `message`.

## 7. Конвенции кода

- C# 12+: **primary constructors** (`public class StockService(IStockRepository repo, TimeProvider clock, ...)`), file-scoped namespace, `record` для DTO.
- Время берётся только через **`TimeProvider`** (в тестах `FixedTimeProvider`), `DateTime.Now` не используем. Даты хранятся как `DateTimeOffset` (UTC).
- Во всех асинхронных методах есть `CancellationToken ct = default`, а у методов суффикс `Async`.
- Enum'ы в JSON сериализуются строками (`JsonStringEnumConverter`).
- Новый сервис: интерфейс в `Application/Interfaces/Services.cs`, реализация в `Application/Services/`, регистрация в
  `Application/DependencyInjection.cs`. Новый репозиторий: интерфейс в `Interfaces/Repositories.cs`, реализация в
  `Infrastructure/Repositories/`, регистрация в `Infrastructure/DependencyInjection.cs`. Для Web — метод в `ApiClient`.
- Комментарии `/// <summary>` пишутся по-русски и объясняют «зачем», а не «что».
- Изменили модель, значит нужна миграция с понятным именем:
  ```
  dotnet ef migrations add <ИмяМиграции> -p SmartWarehouse.Infrastructure -s SmartWarehouse.API -o Data/Migrations
  ```
  Миграции сейчас: InitialCreate → … → AddItemNotes → ClearAutoPlaceNames → **AddInventory** (последняя, 20261125183112).

## 8. Конфигурация

| Ключ | Где | Назначение |
|---|---|---|
| `ConnectionStrings:Default` | API (user-secrets `smartwarehouse-api` / env `ConnectionStrings__Default`) | Строка подключения; **в репозитории её нет** |
| `Database:MigrateOnStartup` | API | Применять миграции и seed при старте |
| `Auth:JwtKey` | API | Ключ JWT, ≥ 32 символов. В Development без него используется постоянный dev-ключ |
| `Auth:TokenHours` | API | Срок жизни токена (по умолчанию 12) |
| `Auth:InitialAdminPassword` | API | Пароль первого администратора |
| `Documents:Organization` | API | Шапка актов |
| `Api:BaseUrl` | Web | Адрес API (dev: `http://localhost:5113`, docker: `http://api:8080`) |
| `DataProtection:KeysPath` | Web | Где хранить ключи cookie между перезапусками |
| `Auth:CookieSuffix` | Web | Суффикс имени cookie для нескольких копий на одном хосте (иначе берётся порт) |
| `App:PublicUrl` | Web | Базовый URL для ссылок в QR-наклейках |
| `Custody:AgeWarningDays` / `WorkReportDays` | Web | Подсветка давнего подотчёта (30 / 14) |

`.env` (из `.env.example`): `POSTGRES_PASSWORD`, `JWT_KEY`, `ADMIN_PASSWORD`, `API_PORT=8080`, `WEB_PORT=8081`, `TZ=Europe/Minsk`, `SITE_ADDRESS`.
`appsettings.Development.json`, `appsettings.Local.json` и `.env` в git не попадают.

## 9. Запуск и команды

```powershell
dotnet restore SmartWarehouse.slnx
dotnet build   SmartWarehouse.slnx
dotnet test    SmartWarehouse.slnx            # Postgres-тесты пропускаются без SW_TEST_DB
$env:SW_TEST_DB="Host=localhost;Port=5432;Username=postgres;Password=...;Database=postgres"; dotnet test

dotnet run --project SmartWarehouse.API       # http://localhost:5113
dotnet run --project SmartWarehouse.Web       # http://localhost:5200 (профиль "lan" открывает доступ с телефона)

docker compose up -d --build                                                    # Linux-сервер
docker compose -f docker-compose.yml -f docker-compose.https.yml up -d --build  # + HTTPS через Caddy

# Демо-данные
dotnet run --project tools/DemoSeed -- "<conn>" seed.json
```

Health-check: `GET /health` у API (проверяет БД и отсутствие непримененных миграций) и у Web.

## 10. CI (GitHub Actions, `.github/workflows`)

- `ci.yml` — на push в `main` и на PR: restore/build/test (Release) с сервисом Postgres 17 и `SW_TEST_DB`, проверка синтаксиса
  `deploy/**/*.ps1`, сборка Docker-образов `Dockerfile` (API) и `Dockerfile.web`.
- `ui.yml` — на PR: DemoSeed → запуск API и Web → Playwright-снимки (компьютер и телефон). Результат публикуется в артефакт
  `ui-snapshots` и в ветку `ci-ui-snapshots`. В `shots/problems.txt` попадают ошибки JS и горизонтальный скролл на телефоне.
- `android.yml` — при изменениях в `mobile/android/**`: `gradle assembleRelease`, APK в артефакт и в релиз `android-latest` (для main).

## 11. Деплой на Windows (основной прод-сценарий)

`deploy/windows/`: `install.ps1 -ServerAddress <IP> [-Instance <имя> -WebPort -ApiPort]`, `update.ps1 [-Branch] [-Instance]`
(делает бэкап, собирает, проверяет health и откатывается автоматически при сбое), `rollback.ps1 [-RestoreDatabase]`, `backup.ps1`
(ежедневно в 02:00, хранит 30 дней), `restore-check.ps1`, `health.ps1`, `uninstall.ps1`, `clear-stock.ps1`.
Приложение ставится в `C:\SmartWarehouse\{api,web}` как службы `SmartWarehouse.Api` / `SmartWarehouse.Web` (`UseWindowsService()`).
Код берётся из `C:\SmartWarehouse-src`, репозиторий: `github.com/venguraserg/SmartWarehouse`.
Скрипты должны проходить проверку синтаксиса PowerShell в CI.

## 12. Подводные камни и заметки для агента

- В API **нет редиректа на HTTPS, и так задумано**: при редиректе HttpClient теряет заголовок `Authorization`.
- Web обращается к API только через `ApiClient`. Фото и PDF проксируются эндпоинтами Web (`/item-image/{id}`, `/act-pdf/{id}`,
  `/document-act/{id}`, `/labels.pdf`, `/act-sample.pdf`), поэтому браузеру не нужен прямой доступ к API.
- `BlazorDisableThrowNavigationException=true` в Web.csproj — так при редиректе на статических страницах отладчик не останавливается.
- Таблица API в `README.md` неполная: там нет контроллеров Inventories, IssueRequests, ItemNotes, Receiving, Import и `stock/summary`.
  Актуальный список смотрите в `SmartWarehouse.API/Controllers/`.
- `mobile/android/app/smartwarehouse.keystore` **намеренно хранится в репозитории**: так все сборки подписаны одним ключом,
  хотя `.gitignore` исключает `*.keystore`. Не удалять.
- Старые наклейки с кириллическими кодами (`СК1`, `ЗЧ-`) и первые `SWL-15` / `SWI-7` всё ещё должны распознаваться (`ScanService`, `LocationCodeMatch`).
  В тестах встречаются кириллические `FullCode` (`СК1-A-01`), это нормально для логики сервисов.
- Поведение ручного сканера: символы приходят с интервалом 5–30 мс, ввод заканчивается Enter или Tab (`keyboard-scanner.js`). Меняя поля ввода, это не ломайте.
- Окружение разработчика: Windows + Visual Studio (multiple startup projects: API и Web), PowerShell.
  Вывод терминала агента иногда не захватывается целиком, поэтому длинные листинги лучше запускать по одной команде.


### Предварительный перечень доработок

Вот список доработок по итогам обзора. Полный аудит кода я не делал, поэтому пункты с пометкой «проверить» — предположения, их нужно подтвердить.

### Безопасность

1. __Убрать ключ подписи Android из репозитория.__ `smartwarehouse.keystore` лежит в git, хотя `.gitignore` исключает `*.keystore`. Лучше хранить его в секретах GitHub Actions (base64) и подписывать APK в CI.
2. __Не стартовать без `Auth:JwtKey` в Production.__ Сейчас API только предупреждает и берёт случайный ключ, и после перезапуска всем приходится входить заново. Надёжнее сразу останавливать запуск.
3. __Убрать пароль `admin123` по умолчанию.__ В `.env.example` и `docker-compose.yml` для сервера лучше требовать `ADMIN_PASSWORD` явно, как уже сделано для `JWT_KEY`.
4. __Ограничить попытки входа.__ Защиты от подбора пароля на `POST /api/auth/login` я не нашёл (проверить). Можно добавить встроенный в ASP.NET Core rate limiter или блокировку после нескольких неудачных попыток.
5. __Добавить Dependabot__ (`.github/dependabot.yml`) для NuGet, npm, Gradle и GitHub Actions.

### Сборка и зависимости

6. __Свести общие настройки в один файл.__ `TargetFramework`, `Nullable` и `ImplicitUsings` повторяются в каждом `.csproj`, их можно вынести в `Directory.Build.props`.
7. __Выровнять версии пакетов.__ `Microsoft.AspNetCore.OpenApi` 10.0.12 стоит рядом с остальными пакетами 10.0.0. Здесь поможет `Directory.Packages.props` (единое управление версиями).
8. __Дополнить `.gitignore`.__ В нём нет `__pycache__/`, а в `tools/opening` уже лежит такая папка.

### Код

9. __Разбить крупные файлы по разделам:__ `Dtos.cs`, `Services.cs`, `Repositories.cs` в Application и `ApiClient.cs` в Web.
10. __Исправить комментарии в `StockController`.__ У метода `Summary` два `<summary>`, причём первый описывает соседний метод `BelowMinimum`.

### Тесты

11. __Добавить тесты API__ через `WebApplicationFactory`: доступ по ролям, скрытие цен (`PriceMaskingFilter`), ответы ошибок. Сейчас тестируются в основном сервисы.
12. __Запускать тесты на PostgreSQL и локально.__ Сейчас без переменной `SW_TEST_DB` они пропускаются. Testcontainers поднимал бы базу автоматически (нужен Docker).

### Документация

13. __Обновить таблицу API в README.__ В ней нет инвентаризации, заявок на выдачу, заметок к позициям, приёмки, импорта и `stock/summary`. Можно ещё включить OpenAPI/Scalar в dev, чтобы список эндпоинтов всегда был актуальным.
14. __Переписать шаг 1 «Миграция БД» в README.__ Миграции уже в репозитории, так что шаг про создание `InitialCreate` устарел.

Самое срочное — пункты 1–4 про безопасность. Пункты 6–8 и 10 быстрые и почти без риска. Могу сразу взяться за любые из них.
