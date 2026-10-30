# Генерация решения

## Установка шаблона

[dotskit](tool.ru.md) делает то же и вдобавок добавляет флаги уже сгенерированным сервисам, сохраняя
правки команды. Эта страница - о шаблоне без инструмента.

Шаблон лежит в папке `template` этого репозитория, рядом со своим `.template.config`:

```bash
dotnet new install /path/to/DotNetSolutionKit/template
```

После получения изменений установите его заново поверх старого:

```bash
dotnet new install /path/to/DotNetSolutionKit/template --force
```

## Генерация

Первый запуск генерирует общие проекты `Common`, корневой файл решения и первый сервис.
Для него передайте `--Solution`:

```bash
dotnet new DotNetSolutionKit -N MyCompany -P MyProduct -S Orders --Solution
```

Каждый следующий сервис добавляется из корня решения. Шаблон в любой команде отвечает и на второе короткое
имя, `dotskit`; инструмент с тем же именем добавляет сервис командой `dotskit new` ([dotskit](tool.ru.md)):

```bash
dotnet new dotskit -N MyCompany -P MyProduct -S Billing
```

Он генерирует папку сервиса и, спросив разрешения запустить команду (`--allow-scripts yes` запускает без
вопроса), добавляет проекты сервиса в `All.sln`, в папку решения по последнему сегменту имени сервиса. Файлы,
которые уже есть в решении, он не трогает: `Common` не получает ничего нового, поэтому сервис берёт только те
флаги, часть которых в `Common` уже есть, а база данных, способ развёртывания и имена передаются заново такими,
какие они у решения.

Сервис, сгенерированный без `--Solution`, не трогает корневой `src/Directory.Packages.props`. В решении, сгенерированном этой версией
шаблона, там уже есть все версии, нужные сервису, с любыми `-H` и `--Messaging`; флаги, которые добавляют
файлы в `Common`, выбираются один раз на решение, см. [параметры](#параметры). В более старом решении
сборка останавливается с `NU1010` и называет пакеты без версии; скопируйте их строки `PackageVersion`
из [`Directory.Packages.props`](../../template/src/Directory.Packages.props) шаблона, а для флага - из его
файла в [`src/package-versions/`](../../template/src/package-versions).

<a id="parameters"></a>
## Параметры

| Параметр | По умолчанию | Что делает |
|---|---|---|
| `-N`, `--NamespaceRoot` | `MyCompany` | Название организации, корневое пространство имён. Может содержать точки. |
| `-P`, `--ProductName` | `Product` | Название продукта. Может содержать точки. |
| `-S`, `--ServiceNameOrCustom` | `Service` | Название сервиса. Может содержать точки. |
| `--Solution` | `false` | Генерирует решение: проекты `Common`, `All.sln` и корневые файлы вместе с первым сервисом. Без него - только папка сервиса, в уже существующее решение. `-M false` прежних версий по-прежнему означает то же. |
| `--Database` | `postgres` | [СУБД](../architecture/persistence.ru.md#sql-server): `postgres` или `mssql` (SQL Server). Одно значение на решение: передайте его с `--Solution` и каждому сервису. |
| `-H`, `--Hangfire` | `true` | [Фоновые задачи](../features/background-jobs.md) на Hangfire. Задаётся для каждого сервиса, в том числе без `--Solution`. |
| `--Messaging` | `none` | [Шина сообщений](../features/messaging.md): `outbox` или `direct`. Задаётся для каждого сервиса. |
| `-I`, `--Infisical` | `false` | [Секреты из Infisical](../features/secrets.md). |
| `--Vault` | `false` | [Секреты из HashiCorp Vault](../features/secrets.ru.md#hashicorp-vault). |
| `--DiffApi` | `false` | [Сравнение контракта API](../features/api-diff.md) в pull request. |
| `--GitHubCiCd` | `false` | [CI на GitHub Actions](../features/ci.md): сборка, тесты на настоящих серверах, покрытие по запросу, поиск секретов. С `--Solution`: workflow покрывают все сервисы в `All.sln`. |
| `-FF`, `--FeatureFlags` | `false` | [Фича-флаги](../features/feature-flags.md). |
| `--HierarchyRules` | `false` | [Правила доступа по дереву тенантов](../features/hierarchy-rules.md). |
| `--Storage` | `false` | [Объектное хранилище](../features/object-storage.md), совместимое с S3. |
| `-CH`, `--ClickHouse` | `false` | [ClickHouse](../features/clickhouse.md): подключения, проверка схемы, готовность. |
| `--MongoDB` | `false` | [MongoDB](../features/mongodb.md) рядом с основной базой: клиент, база сервиса, готовность. |
| `--Notify` | нет | Каналы [уведомлений](../features/notifications.md): `email` - через SMTP или Graph API, с песочницей вне Production; с `--Messaging` другие сервисы просят письмо командой шины. |
| `--TestFramework` | `nunit` | Тестовый фреймворк для тестов сервиса, `nunit` или `xunit` (v3); задаётся для каждого сервиса. Собственные тесты `Common` остаются на NUnit. См. [тестирование](../architecture/testing.md). |
| `--Audit` | `false` | [Журнал аудита](../features/audit.md) изменений сущностей; действует только с `--Messaging outbox`; без него генерация сообщает, что флаг не применён. |
| `--ApiGateway` | `false` | [API-шлюз](../features/api-gateway.md) на YARP вместо сервиса. Только без `--Solution`; с ним генерация сообщает, что флаг не применён. |
| `--Deploy` | `compose` | [Файлы развёртывания](../operations/deployment.md): `compose`, `k8s` или `none`. Одно значение на решение: передавайте то же самое каждому следующему сервису. |
| `--Agent` | `claude` | [Правила и скиллы для ИИ-агента](agents.md): `claude`, `opencode` или `none`. С `--Solution`. |
| `--HttpPort` | свободный порт | Порт в `launchSettings.json` и на хосте под compose. Без параметра берётся свободный порт из диапазона 5000-5999 на машине, где идёт генерация, поэтому сервисы, сгенерированные один за другим, не получают один и тот же порт. |

`-I`, `--Vault`, `--DiffApi`, `--FeatureFlags`, `--HierarchyRules`, `--Storage`, `--ClickHouse` и `--Audit` добавляют файлы в `Common`,
а `--MongoDB` и `--Notify` - проект в `src/capabilities`, поэтому их нужно передать с `--Solution`, когда
генерируется общий код. Каждому сервису,
сгенерированному позже, которому они нужны, передайте `-I`, `--Vault`, `--DiffApi`, `--FeatureFlags`, `--Storage`,
`--ClickHouse`, `--MongoDB`, `--Notify` и `--Audit` ещё раз: они меняют и код сервиса, и сервис подключает то, что уже есть в
`Common` и `src/capabilities`. Сервис, сгенерированный без них, обходится без этих частей.

## Имена с точками

Любой из `-N`, `-P` и `-S` может содержать точки:

```bash
dotnet new DotNetSolutionKit -N Acme.Corp -P Shop.Online -S Sales.Orders --Solution
```

Эта команда генерирует `Acme.Corp.Shop.Online.Sales.Orders.API` и остальные проекты под тем же именем.
Название сервиса с точкой позволяет разбить домен на небольшие сервисы, например `Sales.Orders` и
`Sales.Invoicing`, вместо одного общего проекта на весь `Sales`. Название продукта с точкой сохраняет
происхождение кода в каждом пространстве имён форка или скачанной копии, где названия репозитория уже нет.

Там, где точка недопустима, имя выводится из названия сервиса:

| Где | Форма | Пример |
|---|---|---|
| Пространства имён, проекты, папки | как задано | `Acme.Corp.Shop.Online.Sales.Orders` |
| Идентификаторы C# | без точек | `SalesOrdersDbContext` |
| Схема базы данных, папка Infisical, имена очередей | в нижнем регистре, точки заменены подчёркиваниями | `sales_orders` |

Сервис называйте по области, а не по его главному агрегату: `-S Basket` с классом `Basket` внутри делает
`Basket` и пространством имён, и типом, и C# не принимает тип там, где видно пространство имён (CS0118).
`Baskets`, `Shopping` или `Sales.Baskets` этого не дают.

<a id="add-the-first-migration"></a>
## Первая миграция

У сгенерированного сервиса есть модель, но нет миграций, поэтому его база данных стартует без таблиц.
Добавьте первую миграцию до первого запуска; базы данных команде не нужно:

```bash
cd src/services/MyCompany.MyProduct.Orders
dotnet ef migrations add Initial \
  -p MyCompany.MyProduct.Orders.Infrastructure \
  -s MyCompany.MyProduct.Orders.Infrastructure \
  -o EntityFramework/Migrations
```

Если сервис сгенерирован с `--Messaging outbox`, миграция создаёт и таблицы outbox. Сервис, запущенный
без миграций, пишет в лог строку уровня fatal с этой командой, а каждый запрос к отсутствующей таблице
падает.

## Настройка и локальный запуск

`appsettings.json` и `appsettings.Local.json` поставляются с пустыми обязательными значениями, и рядом с
каждым лежит ключ `_comment_*` с описанием, что туда вписать. Локальные значения положите в
`appsettings.Secrets.json` в той же папке. Он читается только в окружении `Local`, сгенерированный
`.gitignore` не пускает его в репозиторий, а переменные окружения его переопределяют.

В остальных окружениях передавайте те же ключи переменными окружения, например
`ConnectionStrings__DefaultConnection`.

Профиль запуска `local` задаёт `ASPNETCORE_ENVIRONMENT=Local`, поэтому обычный `dotnet run` читает файл
секретов:

```bash
dotnet run --project src/services/MyCompany.MyProduct.Orders/MyCompany.MyProduct.Orders.API
```

Неправильно настроенный сервис останавливается при старте, а не на первом запросе; см.
[проверки при старте](../operations/startup-checks.md).

Чтобы запустить сервис раньше, чем появятся его база данных, брокер или хранилище секретов, отключите их в
конфигурации, например `Database__Enabled=false`; см. [отключение зависимостей](../operations/switching-dependencies-off.md).
