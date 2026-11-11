# Устройство решения

Как сгенерированное решение выглядит на диске, какой файл за что отвечает и где живёт код команды. Имена ниже
берутся из генерации: `-N ST -P Demo -S Orders` даёт `ST.Demo.Common`, `ST.Demo.Orders` и
`src/services/ST.Demo.All.sln`.

## Папки

```
ST-Demo/
├── .dotskit/manifest.json         из чего сгенерировано решение; dotskit читает его на каждом запуске
├── Directory.Build.props          свойства всех проектов и DotNetSolutionKitVersion
├── Dockerfile                     один образ на все сервисы
├── version.json                   версия продукта, ставится руками
├── src/
│   ├── Directory.Packages.props   версия каждого пакета, объявленная один раз
│   ├── package-versions/*.props   версии, которые приносит флаг, по файлу на флаг
│   ├── common/                    то, что общее у сервисов
│   │   ├── ST.Demo.Common/                  примитивы домена: сущности, агрегаты, события, Money
│   │   ├── ST.Demo.Common.Contracts/        то, что пересекает границу: запросы, ответы, сообщения шины
│   │   ├── ST.Demo.Common.Application/      сценарии над доменом и порты, которые им нужны
│   │   ├── ST.Demo.Common.Infrastructure/   EF Core, шина, хранилище секретов, внешний мир
│   │   ├── ST.Demo.Common.Web/              хост: конвейер, ошибки, Swagger, аутентификация
│   │   ├── ST.Demo.Common.Testing/          тестовая оснастка, без тестового фреймворка
│   │   └── ST.Demo.Common.Tests/            тесты Common
│   ├── capabilities/              проект на флаг, сгруппированы по системе: Persistence (база данных, Mongo, S3), Analytics (ClickHouse), Messaging (Notifications), Security (CredentialStore), рядом .Abstractions и .Tests
│   └── services/
│       ├── ST.Demo.All.sln      все проекты решения
│       └── ST.Demo.Orders/      один сервис, четыре проекта (ниже)
├── gateway/                       шлюз API, при --ApiGateway
├── deploy/
│   ├── compose/                   docker compose: сервисы и infra/ по файлу на часть
│   ├── k8s/                       манифесты, при --Deploy k8s
│   └── build-images.sh            собирает образ каждого сервиса
├── tests/servers/                 тестовые серверы: start, ready, logs, down; up.sh поднимает все
├── scripts/                       проверки и схемы решения
├── tools/                         инструменты разработки решения
├── .github/workflows/             CI, при --GitHubCiCd
└── CLAUDE.md                      правила, по которым работает ИИ-агент этого решения
```

## Один сервис

Сервис - это четыре проекта, и зависимости между ними направлены внутрь: домен ничего не знает про веб, а
слой сценариев - про EF Core.

```mermaid
flowchart TD
    API["Orders.API<br/>контроллеры, Program, appsettings"] --> App["Orders.Application<br/>сценарии, порты"]
    API --> Infra["Orders.Infrastructure<br/>EF Core, шина, внешний мир"]
    App --> Dom["Orders<br/>домен: сущности, агрегаты, события"]
    Infra --> App
    Tests["Orders.Tests"] --> App
    Tests --> Infra
    App --> CApp["Common.Application"]
    Dom --> CDom["Common"]
    Infra --> CInfra["Common.Infrastructure"]
    API --> CWeb["Common.Web"]
```

| Проект | Что внутри | Что здесь пишет команда |
|---|---|---|
| `ST.Demo.Orders` | домен: сущности, агрегаты, доменные события, спецификации | модель продукта |
| `ST.Demo.Orders.Application` | сценарии и порты, которые они вызывают | что продукт делает, по шагам |
| `ST.Demo.Orders.Infrastructure` | `DbContext`, репозитории, шина, интеграции | как он достаёт внешний мир |
| `ST.Demo.Orders.API` | контроллеры, `Program.cs`, настройки | endpoints |
| `ST.Demo.Orders.Tests` | тесты этого сервиса | то, что доказывает написанное выше |

## Шаблон и решение

Шаблон генерирует `Common` и корень один раз, а сервис - за запуск; `dotskit` держит манифест того, из чего
сгенерировано решение, и позже сливает в него новую версию шаблона.

```mermaid
flowchart LR
    T["Шаблон<br/>dotnet new DotNetSolutionKit"] -->|"--Solution"| Root["Корень и Common<br/>src/common, deploy, scripts, CI"]
    T -->|"-S Orders"| S1["Сервис<br/>src/services/ST.Demo.Orders"]
    T -->|"-S Billing"| S2["Ещё один сервис"]
    Root --> M[".dotskit/manifest.json"]
    S1 --> M
    S2 --> M
    M -->|"dotskit upgrade"| U["Шаблон новой версии,<br/>слитый в решение"]
```

Флаг добавляет свой проект в `src/capabilities` и свои версии в `src/package-versions`, а сервисы, которые им
пользуются, ссылаются на него - поэтому сервис несёт только пакеты своих флагов.
