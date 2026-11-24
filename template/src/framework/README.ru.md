# Framework

Системы работы, которые приносит шаблон: включены всегда и используются как есть - кандидаты на
отдельный пакет. Пространство имён называет род: `NamespaceRoot.ProductName.Framework.*`, так же
как `NamespaceRoot.ProductName.Capabilities.*` называет capability.

- `NamespaceRoot.ProductName.Framework.DomainEvents` - отделяемый шов трёхфазных доменных событий
  (ADR-001): интерфейсы обработчиков фаз, диспетчер и интерфейс хранения событий. EF Core-пайплайн,
  диспетчеризующий фазы (интерцепторы в `Common.Infrastructure/Persistence/EntityFramework/Events`),
  и примитивы доменных событий (`IDomainEvent`, `IHasDomainEvents`, `EventfulEntity`, `AggregateRoot`
  в `Common`) остаются там, где сплетены с пайплайном `DbContext` и ядерным контекстом исполнения;
  см. Readme проекта.
- `NamespaceRoot.ProductName.Framework.Auth` - аутентификация и авторизация: JWT bearer и API-ключ,
  cookie токенов с их CSRF-контрактом и проверка прав, которую объявляет контроллер.
- `NamespaceRoot.ProductName.Framework.Observability` - идентификатор корреляции: окружающее
  значение, которое идёт через запрос, задачу и сообщение шины, заголовок, несущий его между
  сервисами, и middleware, читающее или создающее его на каждый запрос. Настройка логирования
  остаётся команде в Common; см. Readme проекта.
- `NamespaceRoot.ProductName.Framework.OpenApi` - стек Swagger: версионированные документы,
  фильтры, страница гейтвея и дамп схемы, который читает проверка контракта.
- `NamespaceRoot.ProductName.Framework.Validation` - проводка FluentValidation: валидаторы сборки
  сервиса идут перед обработчиком, плохой запрос отвечает в общей форме.
- `NamespaceRoot.ProductName.Framework.FeatureFlags`, с `--FeatureFlags` - платформенные feature
  flags: общий каталог `features.json` и проводка Microsoft.FeatureManagement.
- `NamespaceRoot.ProductName.Framework.Testing` - переносимая тестовая система (ADR-005): контексты
  исполнения, тестовая проводка доменных событий, заглушки и проверки правил. Хелперы, привязанные
  к внешней системе (тестовые базы PostgreSQL и SQL Server), и аудит-верификаторы остаются в
  `NamespaceRoot.ProductName.Common.Testing`; см. Readme проекта.

У каждого модуля один скелет: его каталог, проект, названный его именем, `.Tests`, когда есть что
тестировать, `README.md`, когда он владеет механизмом, и нужные ему версии в
`src/package-versions/<module>.props`. Кроме `FeatureFlags`, который сервис берёт с `--FeatureFlags`,
ничего здесь не за флагом: каждый сгенерированный сервис ссылается на эти проекты, как на проекты
Common.
