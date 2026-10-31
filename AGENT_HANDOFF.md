# Передача контекста DotNetChannel

Снимок контекста: 31 октября 2026. Это не показания текущего состояния системы.

## Назначение

Автоматически получать содержательные посты из Telegram-канала, объяснять сложные темы C#/.NET
понятным русским языком и публиковать в собственном канале. Аудитория знает основы C#.
Пользователь хочет работающий .NET solution, удобное управление через десктопную админку,
автоматическую обработку и возможность продолжать работу с другим агентом/на другом компьютере.

Путь на исходной машине: `C:\Projects\DotNetChannel`. Он не должен быть обязательным на новой.

## Уже созданные ресурсы

- Донор: https://t.me/NetDeveloperDiary
- Собственный канал: https://t.me/ponyatny_dotnet_diary
- Бот: https://t.me/ponyatny_dotnet_editor_bot
- Первая публикация: https://t.me/ponyatny_dotnet_diary/2

Бот — администратор с правом публикации, остальные дополнительные права выключены.
Управление через личный чат ограничено AdminUserId из локальных настроек.
Эти ресурсы уже существуют; для продолжения не создавайте новые по умолчанию.

## Архитектура

- `Channel.Core`: SourcePost/Rewrite/Draft, ограничение длины публикации.
- `Channel.Infrastructure`: парсер публичного preview Telegram (HtmlAgilityPack), Responses API
  через HttpClient, Telegram Bot API, SQLite Store и read-only DashboardReader.
- `Channel.Worker`: получение, очередь, фильтрация/редактура ИИ, автопубликация либо кнопки
  согласования, дневной лимит попыток, обработка команд, RuntimeMonitor.
- `Channel.Launcher`: WinExe без консоли; запускает worker, передаёт ключ из пользовательского
  окружения при необходимости, перенаправляет журналы и ждёт завершения.
- `Channel.Admin`: WPF .NET 10 Windows; статус, очередь/поиск, тексты и замечания, журналы,
  сведения Telegram, штатные Start/Stop/Restart через COM Task Scheduler.
- `Channel.Tests`: на дату снимка 31 проходящий тест.

База `data/channel.db`: таблица posts(id, state, payload JSON, error), таблица meta(key,value).
Только один донор на папку данных: ID поста пока является единственным первичным ключом.
Meta хранит cursor, offset, attempts:YYYY-MM-DD (UTC), published:SOURCE_ID -> message ID,
sourcePollUtc и sourceError. Не теряйте базу при перезапуске/переносе.

Состояния очереди и важные инварианты описаны в навыке. При неизвестном результате отправки
используется Uncertain, без автоматического повторения. Кнопки проверяют владельца и ревизию.

## Текущая конфигурация и секреты

В оригинальном settings.local.json настроены AutoPublish=true, модель gpt-5.2,
PollSeconds=120, MaxDraftsPerDay=3. Все попытки, включая ошибки/переработки, считаются по UTC.
На 31 октября 3 попытки были использованы для настройки первой статьи; не считайте это
постоянным состоянием бюджета.

BotToken и AdminUserId находятся в локальном файле. OpenAI использует существующую
OPENAI_API_KEY из окружения Windows, ранее использованную LiveCaptioner. Сам LiveCaptioner
не нужен для работы и не изменялся. Значения секретов не копируются в этот документ и навык.
На новом компьютере восстановите их отдельно с пользователем. Текстовая модель не должна
подменяться моделями распознавания речи из старого проекта.

## Запуск и остановка

Задача Windows `DotNetChannel`: логон текущего пользователя + 30 секунд, launcher из Release.
До трёх рестартов при аварии; ручная штатная остановка завершает worker с кодом 0.
Админка открывается ярлыком `DotNetChannel Admin` на рабочем столе или в корне проекта.
F5 обновляет, F6 запускает, F7 останавливает, F8 перезапускает. Закрытие окна сервис не выключает.

RuntimeMonitor пишет data/runtime.json; админка атомарно пишет PID в data/stop.request.
При stop совпадение PID отменяет linked CancellationToken. Монитор терпит временные блокировки
файла и при необработанной ошибке отменяет worker, а не оставляет его без контроля.
Время интерфейса — Киев через Windows FLE Standard Time. Календарь бюджета — UTC.

В момент подготовки переносимого навыка задача Windows была **Ready**, runtime — **Stopped**.
Это может быть ручной выбор пользователя. Экспорт/документация не включают сервис заново.
При продолжении проверьте живое состояние, не ориентируйтесь на старый PID/скриншот.

## Проверенные команды

```powershell
dotnet build DotNetChannel.sln -c Release
dotnet test tests/Channel.Tests/Channel.Tests.csproj -c Release
dotnet run --project src/Channel.Worker -- --preview
dotnet run --project src/Channel.Worker -- --check
Get-ScheduledTask -TaskName DotNetChannel
Start-ScheduledTask -TaskName DotNetChannel
Stop-ScheduledTask -TaskName DotNetChannel
```

Сборка может быть заблокирована работающим exe/dll. Сначала выясните, что запущено, и остановите
только относящиеся к проекту процессы. Не удаляйте data и не меняйте настройки безопасности Windows.

## Ограничения и проверенные исправления

- Источник читается через публичный веб-preview, без Telegram-аккаунта и api_id. Это не стабильный
  API. Catch-up ограничен 20 страницами; при неразрешимом пропуске cursor не продвигается.
- Только текст/код/ссылки. Внешние статьи, изображения, видео, альбомы и правки старых постов
  не обрабатываются. Анонсы без достаточного текста должны отклоняться.
- Структурированный ответ ИИ и его самопроверка не гарантируют фактическую точность.
  Первая статья дополнительно проверена тестом: check()=3 в обоих вариантах, Message=null/3.
- reply_markup:null Telegram отклоняет: сериализация должна пропускать null-поля.
- Файловая блокировка запроса остановки не должна выключать монитор — есть regression test.
- На исходной Windows Europe/Kyiv не распознался; применён FLE Standard Time.
- Скриптовый запуск блокировался проверкой подписи. Решение — native launcher, не ослабление policy.
- Native computer-use screenshot не работал, AX и клавиатура работали. Telegram Web после
  входа пользователя по QR позволил закончить настройку. Не обходите аутентификацию.
- Админка умеет `--snapshot`: сохраняет собственный WPF-рендер в data/admin-preview.png.

## Что читать дальше

`skills/telegram-channel-studio/SKILL.md` — переносимый рабочий процесс.
`PORTABILITY.md` — установка навыка, экспорт и новый компьютер.
`ADMIN.md` — десктопная админка; `OPERATIONS.md` — эксплуатация.

Перед существенными правками сначала прочитайте текущий код: этот снимок не заменяет исходники.

Навык и helper дополнительно проверены валидатором Skill Creator и 4 Python-тестами (очистка секретов, WAL backup, защита от перезаписи навыка).

Правило оформления: не добавлять в конечную публикацию ссылку на донорский Telegram-пост/канал. Внешний первоисточник статьи сохраняется в тексте. Внутренняя Source.Url остаётся в базе и админке.

Архив: ArchiveScanner читает TelegramSource.ReadPage с data-before и PublishedUtc из time[datetime]. В админке период (дни Киева) либо последние N постов, preview и выбор перед enqueue. Никакого ИИ при scan. Явный enqueue использует атомарный INSERT OR IGNORE и не меняет cursor; реклама и прочее отсекаются существующим AI relevant=false, Skipped/Failed тоже считаются известными. SourcePost получил необязательный PublishedUtc, старый JSON совместим. Верхний предел прохода 50 страниц, ограниченность результата сообщается пользователю.

UI defaults to English with live EN/RU switching (Ctrl+Shift+L) and data/admin.preferences.json.
Interface locale never changes article language. UiText loads embedded Localization/en.json;
keys are Russian source strings, XAML resource keys are stable SHA-256 prefixes.
UsageLedger stores each AI attempt in ai_usage via the injected Store, then records response.usage
before output/status validation. DashboardReader reads it in the same read-only snapshot.
Historical/missing usage stays unknown, not zero. Cached/reasoning subsets must not be double-counted.
Usage & limits shows recorded UTC-day/all-time totals and app requests remaining. The user explicitly
chose no daily token budget; Not set is correct. x-ratelimit-remaining-tokens is a sampled rate-window
value, never account credit. Preserve the distinction. Current .NET tests: 31.
