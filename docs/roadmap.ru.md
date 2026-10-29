# Дорожная карта

Выпущенные релизы, текущий шаг и запланированные шаги по порядку. Ось считает шаги, а не даты: чем длиннее полоса, тем больше шаг. Полосы, стоящие рядом, можно делать параллельно.

```mermaid
gantt
    dateFormat X
    axisFormat %s
    tickInterval 1second

    section Done
    v1.0 the template before 2026                         :done, s1, 0, 3000ms
    v2.0 layered services, shared host, flags             :done, s2, after s1, 3000ms
    v2.1 deployment, API gateway, CI, correlation         :done, s3, after s2, 2000ms
    v2.2 idempotent commands                              :done, s4, after s3, 500ms
    v2.3 surviving a secret store outage                  :done, s5, after s4, 500ms
    v2.4 tokens in cookies, CSRF                          :done, s6, after s5, 500ms
    v2.5 audit journal                                    :done, s7, after s6, 1000ms
    v2.6 NUnit or xUnit                                   :done, s8, after s7, 1000ms
    v2.7 site, samples, nuget.org, major check, gateway (#5), Vault, settings reloaded, SQL Server :done, s9, after s8, 4000ms
    v2.8 dotskit new, init and upgrade; a service adds itself to All.sln; MongoDB, email, read-only containers, coverage on demand :done, s10, after s9, 4000ms

    section Now
    2.9 - dotskit upgrade adds the project references and using lines of types the template moved (#117) :active, s32, after s10, 1500ms
    template.yml starts every generated service and waits for it to be healthy (#107) :s33, after s10, 500ms
    bluegreen.sh says what is missing when a solution has no service to deploy (#103) :s34, after s33, 300ms
    a table of flags - what a flag requires, excludes or gives with another (#113) :s35, after s34, 500ms
    Samples - a branch that lives a product's life, upgraded by dotskit (#104) :s36, after s10, 1500ms

    section Next
    3.0 on .NET 8 - shared code by kind, the configuration store, flags in lower case (#101, #105, #106) :s15, after s32, 3000ms
    4.0 on .NET 10; 3.x stays on .NET 8 until 10 November 2026 :s31, after s15, 3000ms

    section Ideas
    push to connected clients through SignalR (#71) :s37, after s15, 1000ms
    document presentation, and templates on top of it (#74) :s38, after s37, 2000ms
    a generated solution releases the way the template does (#64, #66) :s39, after s38, 1500ms
```

Что принёс каждый релиз: [version.json](https://github.com/sawking-tech/DotNetSolutionKit/blob/master/version.json).
Работа идёт в `dev`, а релиз попадает в `master` примерно раз в неделю ([CONTRIBUTING](https://github.com/sawking-tech/DotNetSolutionKit/blob/master/CONTRIBUTING.md)).
С 2.8 `dotskit` ведёт решение вслед за шаблоном всё время, пока решение живёт: `dotskit new` добавляет в него
сервисы и флаги, `dotskit init` описывает решение, сделанное без инструмента, а `dotskit upgrade` обновляет его
до новой версии, сливая изменения шаблона с правками команды. Всё из Now входит в 2.9, на .NET 8. 3.0 остаётся на .NET 8
и делит `Common` на проекты по возможностям, чтобы сервис нёс только те пакеты, которыми пользуется; решение
2.x обновляется на неё через `dotskit`, который с 2.9 добавляет ссылки на проекты и строки `using` для типов, перенесённых шаблоном. 4.0 переходит на .NET 10.
Поддержка .NET 8 заканчивается 10 ноября 2026 года: 3.0 и 4.0 выходят до этой даты, после неё 3.x получает
только исправления. У каждой идеи есть задача, версии пока нет.

| Версия | Что меняется для решения |
|---|---|
| 2.8 | `dotskit` держит решение в ногу с шаблоном: `dotskit new` добавляет сервис или флаг и сохраняет правки команды, `dotskit init` описывает решение, сделанное без него, `dotskit upgrade` обновляет решение до своей версии; сервис, добавленный одним шаблоном, сам встаёт в `All.sln`, без скрипта; MongoDB и почта по флагу, у каждой свой проект в `src/capabilities`; контейнеры с файловой системой только для чтения; тестовые серверы запускаются одной командой, локально так же, как в CI; покрытие по запросу; `--Solution` вместо `-M false`; шаблон отвечает и на короткое имя `dotskit` (`dotnet new dotskit`) |
| 2.9 | `dotskit upgrade` собирает решение после слияния и добавляет ссылки на проекты и строки `using`, которые нужны перенесённому типу, по индексу того, где лежит каждый тип шаблона; что осталось, перечисляется по проектам; обновляется решение любого выпуска начиная с 1.0.0, выпуски до nuget.org берутся из тегов, `--to` ведёт по выпуску за шаг (#117); `dotskit new` и `dotskit upgrade` сливают через git по ветке `dotskit/template`, конфликт разрешается в IDE, а `--merge files` оставляет собственное слияние инструмента; `bluegreen.sh` говорит, чего не хватает, когда разворачивать нечего (#103); в доке есть таблица того, что флаг требует, исключает или даёт вместе с другим (#113); файлы шаблона в UTF-8 с концами строк LF, переводом строки в конце и без пробелов в конце строк, BOM только у файла решения; `dotskit upgrade` приносит это в решение, сначала приводя к стилю все три стороны файла, поэтому одна смена стиля не конфликтует ни с чем, что написала команда |
| 3.0 | Общий код раскладывается по тому, что он такое. `src/framework` - системы, которые шаблон приносит как способ работы, есть всегда и используются как есть: трёхфазные доменные события ([ADR-001](adr/001-three-phase-domain-events.ru.md)) и система тестирования ([ADR-005](adr/005-testing-a-service.ru.md)). `src/capabilities` - то, что включает флаг, проект на флаг, настраивается, но не правится: фичефлаги, MongoDB, почта, ClickHouse, объектное хранилище, журнал аудита; выключенный флаг убирает свой проект целиком. `src/common` - общее для сервисов продукта, которое команда меняет под себя: контракты, базовый контекст базы, веб-конвейер. Пространство имён называет вид (`NamespaceRoot.ProductName.Capabilities.Mongo`). Хранилище секретов становится хранилищем конфигурации. `-M` уходит (`--Solution` есть с 2.8), флаги пишутся в нижнем регистре (`--api-gateway`), пакет называется `SawKing.DotsKit.Templates`. На неё решение последнего 2.x переводит `dotskit upgrade`, и его починка из 2.9 добавляет ссылки и строки `using` для переехавших типов |
| 4.0 | .NET 10 |

## Меньше привязки к технологиям

Технологии по умолчанию выбраны по вкусу автора. Команда со своим стандартом должна иметь возможность их заменить.

| Технология | Где шаблон от неё зависит сейчас |
|---|---|
| PostgreSQL | нет: `--Database mssql` генерирует решение на SQL Server, каждая из этих частей за тем же швом, см. [хранение](architecture/persistence.ru.md#sql-server) |
| Infisical | нет: `--Vault` читает так же из HashiCorp Vault, через тот же порт `ISecretStore`, см. [секреты](features/secrets.ru.md#hashicorp-vault) |
| GitHub CI | только сгенерированный workflow; проверки - это скрипты, которые вызовет любой CI |
