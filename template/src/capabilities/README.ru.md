# Capabilities

Проект на флаг шаблона, который приносит свой код: `Capabilities.Messaging.Notifications` с
`--Notify email`, `Capabilities.Persistence.Mongo` с `--MongoDB`, `Capabilities.Analytics.ClickHouse`
с `--ClickHouse`. Каждый проект несёт свой README. Сервис ссылается только на используемые
capabilities, поэтому несёт только их пакеты. Каталог на месте даже пустым: сборка образа и его тег
могут на него рассчитывать.
