# Уведомления

`--Notify` даёт сервису уведомления и выбирает их каналы; `--Notify email` - почта, SignalR
[в планах](https://github.com/sawking-tech/DotNetSolutionKit/issues/71) вторым каналом. Флаг передаётся
сервису, который владеет уведомлениями. Сообщение уходит получателю как есть: тема и простой текст. Письма
из шаблонов придут с `--Templates`, который [в планах](https://github.com/sawking-tech/DotNetSolutionKit/issues/74)
поверх `--Documents`.

| Часть | |
|---|---|
| `INotificationEmailSender` | порт в `Capabilities.Notifications` - отдельном проекте, на который ссылается сервис с `--Notify email`; остальной `Common` ему не нужен: `SendEmailAsync` и `SendEmailWithAttachmentAsync` |
| транспорты | SMTP на MailKit и Microsoft Graph, внутренние; тот, что назван в `Email:Provider`, выбирается один раз при регистрации |
| песочница | вне Production каждое письмо уходит на один адрес с пометкой сверху: кому оно было, окружение и время; ни один транспорт её не обходит |

```csharp
public sealed class InvoiceMailer(INotificationEmailSender email) : IInvoiceMailer
{
    public Task SendAsync(Invoice invoice, byte[] pdf, CancellationToken ct) =>
        email.SendEmailWithAttachmentAsync(invoice.CustomerEmail, $"Invoice {invoice.Number}",
            "Your invoice is attached.", pdf, $"{invoice.Number}.pdf", ct);
}
```

Graph принимает вложение в том же запросе, что и письмо. Microsoft описывает этот способ для файлов меньше
3 МБ; файлу больше нужна
[сессия загрузки](https://learn.microsoft.com/en-us/graph/outlook-large-attachments), которую транспорт не
открывает; для вложения больше нужен SMTP.

## Запрос из другого сервиса

С `--Messaging` другой сервис просит письмо командой шины `SendEmailCommandV1` из `Common.Contracts`;
синхронно владельца никто не зовёт. Её потребитель, `SendEmailCommandV1Consumer` в Infrastructure
владельца, отправляет письмо через порт, так что песочница действует и на него. С `--Storage` команда несёт
вложение ключом в объектном хранилище и именем файла: отправитель сначала кладёт файл туда, а если файл не
читается или отправка с ним падает, письмо уходит без вложения. Отправитель и владелец читают один бакет,
`S3:BucketName`.

Потребитель получает каждый сервис, сгенерированный и с `--Notify email`, и с `--Messaging`, а очередь команды
без префикса сервиса: два таких сервиса делят команды между собой, и каждый отправляет со своими настройками
`Email`. Оставьте потребитель в одном сервисе; сервису, который только отправляет через свой порт, он не нужен.

```csharp
await bus.SendAsync(new SendEmailCommandV1
{
    Id = Guid.NewGuid(),
    OccurredOnUtc = time.GetUtcNow(),
    ToEmail = customer.Email,
    Subject = $"Invoice {invoice.Number}",
    Body = "Your invoice is attached.",
    AttachmentKey = $"invoices/{invoice.Id}.pdf",
    AttachmentName = $"{invoice.Number}.pdf",
}, ct);
```

## Песочница

Тестовый стенд не должен писать никому снаружи. Каждый транспорт наследует одну базу, чьи методы применяют
песочницу и только потом передают письмо провайдеру: вне Production или пока `Email:Sandbox:Enabled` равно
`true`, письмо уходит на `Email:Sandbox:ReceiverAddress` с пометкой сверху (`[SANDBOX INTERCEPTED]`, исходный
получатель, окружение, время по UTC). Production пишет настоящим получателям, только когда песочница
выключена.

## Настройки

Проверяются на старте: провайдер должен быть назван, в его секции должно быть всё нужное, а адреса (`From`,
`SenderEmail`, `Sandbox:ReceiverAddress`) должны быть адресами почты. Вход на SMTP-сервер задаётся парой
`Username` и `Password` или не задаётся вовсе: с половиной пары транспорт пропустил бы вход, и каждая
отправка падала бы.

| Настройка | |
|---|---|
| `Email:Provider` | `Smtp` или `GraphApi` |
| `Email:TimeoutSeconds` | 30: SMTP, на каждую сетевую операцию доставки; Graph его не читает |
| `Email:Smtp:Host`, `Email:Smtp:Port`, `Email:Smtp:From` | сервер и отправитель; по умолчанию порт 587 |
| `Email:Smtp:Secure` | `true`: TLS при подключении на любом порту; иначе 465 - TLS при подключении, 587 - STARTTLS |
| `Email:Smtp:Username`, `Email:Smtp:Password` | только для сервера, который требует входа, из хранилища секретов |
| `Email:GraphApi:TenantId`, `ClientId`, `ClientSecret` | приложение, которое отправляет, секрет из хранилища секретов |
| `Email:GraphApi:SenderEmail` | ящик организации, от имени которого уходят письма |
| `Email:Sandbox:Enabled` | по умолчанию `true`; вне Production песочница действует при любом значении |
| `Email:Sandbox:ReceiverAddress` | куда уходят перехваченные письма |

Лежащий почтовый сервер не выводит сервис из трафика: у почты нет проверки готовности, а неудачная
доставка роняет только тот вызов, который её запросил.

Под docker compose инфраструктура получает [MailHog](https://github.com/mailhog/MailHog): он ловит все письма
и показывает их на `http://127.0.0.1:8025`; `SMTP_HOST` и остальные `SMTP_*` и `EMAIL_*` в
`deploy/compose/.env` называют вместо него настоящий сервер.

## Тесты

Тест сервиса даёт ему двойник `INotificationEmailSender`, как любого другого порта; тесты владельца
проверяют, что команда, отправленная в шину на её очередь, доходит до потребителя `SendEmailCommandV1`, а он отправляет
то, что сказано в команде, с вложением и без. `Capabilities.Notifications.Tests` проверяет
ядро: какие настройки обязательны, транспорт названного провайдера, что SMTP- и Graph-транспорты передают
клиентам, защиту по порту и песочницу; и на настоящем MailHog из `TEST_SMTP` и `TEST_SMTP_API` - что письмо
в Production приходит получателю, а в других окружениях на адрес песочницы.

```bash
eval "$(bash tests/servers/up.sh)"     # тестовые серверы, среди них MailHog (tests/servers/mailhog.sh)
dotnet test --filter "FullyQualifiedName~Smtp"
```
