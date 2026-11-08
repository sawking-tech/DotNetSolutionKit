---
name: release
description: Выпуск версии шаблона DotNetSolutionKit - проверка условий, чеклист текстов и метаданных, PR dev -> master в конце спринта, тег, публикация, проверка по существу nuget.org, GitHub-релиза, GitHub Packages, сайта и Samples. Применять только когда Володя говорит выпустить, зарелизить или выложить в NuGet; мерж в master = релиз.
---

# Релиз шаблона

**Мерж в `master` = релиз.** Что пришло в `master`, публикуется: артефакты, GitHub-релиз, пакеты на nuget.org
и GitHub Packages, пересборка Samples. Версия в `master` без публикации - сломанное состояние: master называет
себя версией, которой нигде нет. Поэтому мерж в `master` - только этим скиллом и только по слову Володи
(память version-bump-is-release). Опубликованное не правится: nuget.org не перерендеривает readme и заметки,
метаданные версии не меняются - ошибку текста исправляет только следующая версия. Поэтому все проверки ниже -
ДО тега.

Схема версий (Володя 05.10, trunk-based, `dev` - trunk, `master` - последний выпуск):

| Где | Версия | Заметки |
|---|---|---|
| `master` | последний выпуск | запись версии |
| `dev` | следующий минор с меткой, `X.Y.0-rc` | одна запись `-rc`, каждое видимое изменение дописывает в неё пункт |
| выпуск | снять метку в `dev` (`X.Y.0-rc` -> `X.Y.0`), перемотка `master` | запись переименовывается, сводить нечего |
| ломающее изменение | `X.0.0-rc` | путь в upgrading |
| хотфикс, `dev` можно выпускать | фикс в `dev`, выпуск `dev` | в запись `dev` |
| хотфикс, `dev` выпускать нельзя | фикс в `master`, патч, выпуск, затем `master` -> `dev` (reconcile) | своя запись патча |

Минор при выпуске растёт ровно на один; патч - только хотфикс. Версии с меткой нигде не публикуются.

## 0. Условия выпуска - каждое проверено делом, иначе не начинать

- [ ] **template.yml зелёный на tip `dev`.** Проверка: `gh run list --branch dev --limit 1 --json workflowName,conclusion --jq '.[] | select(.workflowName=="Template")'`.
- [ ] **full-dev пересобран из tip `dev`, его CI зелёный.** Проверка: `git show origin/full-dev:template-version` == `git rev-parse origin/dev`; `gh run list -R sawking-tech/DotNetSolutionKit.Samples --branch full-dev --limit 3` все success. Если нет: `gh workflow run regenerate.yml -R sawking-tech/DotNetSolutionKit.Samples --ref full -f force=true` и дождаться.
- [ ] **Скилл шаблона в решении под выпуск**: `template/.claude/skills/scaffold-service/SKILL.md` сверен с этой версией (флаги, команды `dotskit`, шаги). Это единственное место в сгенерированном решении, которое знает про шаблон.
- [ ] **Пачка закончена**: пункты плана этого выпуска закрыты, в `dev` нет начатого и недоделанного.
- [ ] **Флаги под версию**: `git diff v<прошлая>..dev -- template/.template.config/template.json` - если появились новые флаги, у каждой ветки Samples, которой они нужны, запись `generate` в `.samples/variants.json` на ветке `full` с `since` не выше выпуска. Без записи ветка пересоберётся из новой версии со старыми флагами.
- [ ] **Чеклист раздела 1 проткнут целиком** - он и есть PR выпуска, и PR принимается только при всех [x].

## 1. Чеклист текстов и метаданных - проткнуть ДО мержа, каждый пункт командой

PR выпуска не принимать, пока не все пункты отмечены. Каждый пропуск здесь - опубликованная ошибка.

- [ ] **version.json**: `"version"` без `-rc`; запись `releaseNotes` с этой версией есть; `headline` и `highlights` на английском; `ru` с тем же числом пунктов.
      Проверка: `jq -r .version version.json` и `jq '.releaseNotes["X.Y.0"] | {en: (.highlights|length), ru: (.ru.highlights|length)}' version.json`.
- [ ] **Заметки - по разделу «Заметки к выпуску» ниже.** Сверить с `git log v<прошлая>..dev`: всё, ради чего меняется номер, есть; остальное не перечисляется. Текст через text-edit, показать Володе до коммита.
- [ ] **nuget/README.md**: пример установки конкретной версии называет выпуск: `grep "::X.Y.Z" nuget/README.md` находит строку. (2.9.0 вышла со строкой `::2.8.0` - строка не поднималась начиная с 2.8; с 2.9 это пункт чеклиста.)
- [ ] **tool/README.md**: секция Stay current (роадмап, звезда обоих репо), кросс-ссылка на пакет шаблона. Проверка: `grep -c "Stay current\|star" tool/README.md`.
- [ ] **README.md**: прямая ссылка на `docs/roadmap.md`, призыв поставить звезду. Проверка: `grep -c "roadmap.md" README.md`.
- [ ] **index.html**: `<meta name="description">`, og и twitter, JSON-LD и «Что вы получаете» называют то, что выпуск приносит (проверить глазами diff); роадмап и звезда - ссылками. (2.7.0 вышла с описанием «on PostgreSQL» без SQL Server и Vault.)
- [ ] **Страница пакета**: `Description` и `PackageTags` в `DotNetSolutionKit.Templates.csproj` и в csproj тулзы называют выпуск.
- [ ] **roadmap.md и roadmap.ru.md**: строка версии переведена в `:done`. Проверка: `grep ":done" docs/roadmap.md docs/roadmap.ru.md` в части этой версии.
- [ ] **Пакет собран и прочитан изнутри ДО тега**:
      `rm -rf /c/t/pack-check && dotnet pack DotNetSolutionKit.Templates.csproj -c Release -o /c/t/pack-check`
      - в `.nuspec` `<releaseNotes>` - текст заметок, не ссылка;
      - README внутри nupkg - новый (`unzip -p /c/t/pack-check/SawKing.DotNetSolutionKit.X.Y.0.nupkg README.md | grep -c "Stay current|strongly recommended"`);
      - версия в примере установки внутри nupkg == выпуск (`... | grep "::X.Y.0"`).
- [ ] **Заметки релиза прочитаны глазами**: `bash scripts/release-notes.sh X.Y.0 plain` и `... markdown` - обе строки футера на месте (upgrading и roadmap).
- [ ] **Сайт собирается**: `python scripts/build-site.py /c/t/site-check` - без ошибок, в выводе новая версия.
- [ ] **style и якоря**: `python scripts/check-style.py` и `python scripts/check-doc-anchors.py` - чисто.

## 2. Мерж и тег

- PR выпуска зелёный (все проверки `template.yml`), мерж squash или rebase, история линейная.
- Перемотка `master` := tip `dev`: `git push --force origin $(git rev-parse origin/dev):refs/heads/master` (защита master на время вырубает Володя; геометрический ff часто невозможен - master разошёлся).
- Тег сразу после перемотки: `git push origin <sha-tip-dev>:refs/tags/vX.Y.0`. Пуш тега - триггер release.yml и больше ничем не ставится; ruleset `release-tags-are-immutable` запрещает двигать существующие теги `v*`.
- `dev` должен уже нести метку снятой версии; после выпуска - коммит в `dev`: следующий `-rc` и пустая запись под него.
- Линии: `master` - текущая мажорная; `2.x` после перехода на .NET 10 - .NET 8 до 10.11.2026. Патч .NET 8 - PR в `2.x`, тег с неё.
- Утверждение в заметках о поведении (обновление, совместимость) проверить делом до коммита: заметка про обновление 2.6.1 -> 2.6.2 оказалась неверной для установки из файла и была исправлена только после проверки в отдельном каталоге шаблонов (`--debug:custom-hive`).

## 3. Проверка по существу, не по зелёному значку

Выпуск не закончен, пока всё ниже не подтверждено командами.

- [ ] `gh run list --workflow release.yml --limit 1` - success; шаги «A short-lived key for nuget.org» и «Publish to nuget.org» выполнены.
- [ ] GitHub-релиз: заголовок `vX.Y.0: <headline>`; приложены ОБА `.nupkg` (`gh release view vX.Y.0 --json assets`). Ты просил: пакеты в GitHub-релизе и в GitHub Packages - проверять оба.
- [ ] GitHub Packages: `gh api "orgs/sawking-tech/packages?package_type=nuget"` - оба пакета, видимость Public. Версия выпуска среди версий ОБОИХ пакетов: `gh api "orgs/sawking-tech/packages/nuget/SawKing.DotNetSolutionKit/versions" --jq '.[].name'` и то же для `SawKing.DotsKit.Tool` содержат `X.Y.0`. (Страница пакетов орга врет: «Published on» там — дата создания пакета, не последняя версия; 2.9.0 лежала в Packages, а страница показывала октябрь.) Если 403 у релиза: настройки пакета -> Manage Actions access -> DotNetSolutionKit с ролью Write (только UI).
- [ ] nuget.org после индексации (до получаса): `curl -s https://api.nuget.org/v3-flatcontainer/sawking.dotnetsolutionkit/index.json` содержит версию; `.../X.Y.0/readme` и `.../X.Y.0/icon` - 200; страница пакета показывает README и Release Notes с новым текстом.
- [ ] **Samples - идеальная чистота (витрина)**: в `DotNetSolutionKit.Samples` только ветки семплов (`full`, `single`, `gateway`, `full-alt`, `full-dev`, `lifecycle`) - ни одной влитой фиче-ветки: `gh api repos/sawking-tech/DotNetSolutionKit.Samples/branches --paginate -q '.[].name'`. Влитые ветки удаляются мержем (`gh pr merge --delete-branch`); свои проверки - оркестратор держит чистым.
- [ ] Установка по ID в отдельном каталоге шаблонов заменяет прошлую версию: `dotnet new install SawKing.DotNetSolutionKit::X.Y.0 --debug:custom-hive /c/t/hive-check` и `dotnet new list DotNetSolutionKit --debug:custom-hive /c/t/hive-check` - один шаблон.
- [ ] Samples: `gh workflow run regenerate.yml -R sawking-tech/DotNetSolutionKit.Samples --ref full`; ветки пересобраны из новой версии, их CI и отчёты зелёные, страница dnsk.sawking.tech/samples.html показывает новую версию.
- [ ] Сайт: `curl -s https://dotskit.sawking.tech/version.json` - новая версия; главная называет выпуск.

## 4. Публикация на nuget.org

С 2.8 ключа в секретах нет: release.yml получает ключ на час через Trusted Publishing (`NuGet/login`, профиль
`sawking.tech`) после проверок и до тега. Политика на nuget.org - профиль `sawking.tech`, Trusted Publishing:
репозиторий `sawking-tech/DotNetSolutionKit`, файл `release.yml`, без environment, `SawKing.*`. Обмен не прошёл -
релиз встаёт до тега: сверить политику (статус Active, имя файла), затем запустить `promote.yml` заново. Если
nuget.org не принимает токен запуска по метке (`pull_request_target`) - запустить `promote.yml` руками
(`workflow_dispatch`).

## Если что-то пошло не так

- Тег не на том коммите и релиз не начался: переместить тег нельзя (ruleset `release-tags-are-immutable`) - только новый номер. Защита от этого - пункты чеклиста ДО тега.
- GitHub-релиз создан, а push на nuget.org упал: перезапустить упавший прогон release.yml (`gh run rerun <id> --failed`); `--skip-duplicate` пропустит уже выложенное.
- Заметки к выпуску на nuget.org - текст версии из version.json (после правки package-page; до неё там была только ссылка на GitHub-релиз). Проверить `unzip -p <nupkg> '*.nuspec'`: в `<releaseNotes>` текст, не ссылка.

## Заметки к выпуску: формат и принципы

Заметки к выпуску и «что нового» - одно и то же (Володя 05.10): `releaseNotes` в `version.json`, из них
GitHub-релиз и nuget.org. Это не аудит и не лог - полный список изменений даёт `git log`.

- Читатель - тот, кто решает обновиться: техлид, архитектор, менеджер продукта. Общепринятые термины
  (API-шлюз, CORS, секреты) - можно; детали реализации (имена классов, заголовков, методов) - нет. Понятно
  не инженеру не значит «для дурака».
- Только существенное - то, что реально меняло номер версии. Остальное - бонус, его не перечисляют.
- Пункт - заголовок изменения, без деталей: «Improved the API gateway.» Детали - в коммитах и доке, шаги
  обновления - в upgrading; кому надо, раскопает.
- Каждый пункт - одна фраза о пользе для того, кто обновляется, с позитивом: что теперь есть или работает,
  а не что убрали или чинили.
- `headline` - главное в версии, одной фразой. `highlights` по весу: можно пять и шесть, но каждый пункт
  должен этого стоить (Володя 05.10: «в этом смысл»).
- У каждой версии рядом с английскими `headline` и `highlights` - русские в `ru`, столько же пунктов: их
  показывает блок «Что нового» на сайте, когда страница по-русски. Без `ru` русская страница молча покажет
  английский. GitHub-релиз и nuget.org берут только английские.
- Несовместимое изменение называется прямо, одним пунктом со ссылкой на раздел в
  `docs/getting-started/upgrading.md`; шаги обновления живут там, не в заметках.

Образец - лента release notes Anthropic (platform.claude.com/docs/en/release-notes): один-пять пунктов на
дату, пункт начинается с того, что пользователь теперь может или что для него изменилось, мелких исправлений
нет, у несовместимого - что делать.
