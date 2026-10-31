# Current reference project: DotNetChannel

This is a dated reference, not live state. Find the actual project root supplied by the user;
the original root was `C:\Projects\DotNetChannel`. Read its `AGENT_HANDOFF.md` for continuation.

Public resources created in the original session:

- Donor: https://t.me/NetDeveloperDiary
- Destination: https://t.me/ponyatny_dotnet_diary
- Bot: https://t.me/ponyatny_dotnet_editor_bot
- First published article: https://t.me/ponyatny_dotnet_diary/2

The configured audience is Russian-speaking developers with basic C# knowledge. The selected
text model at handoff is `gpt-5.2`. Credentials reside only in local configuration and the user
environment. Live mode had AutoPublish=true; transfer templates intentionally default to false.
Do not infer today's desired running state from this note.

The solution has Core, Infrastructure, Worker, Launcher, Admin (WPF) and Tests projects.
Local documentation is `README.md`, `OPERATIONS.md`, `ADMIN.md`, and `PORTABILITY.md`.
There are 24 tests at this snapshot. Preserve the critical invariants described in pipeline.md.

The Windows task is named `DotNetChannel`; the admin shortcut is `DotNetChannel Admin`.
The service is an ordinary background user process managed by Task Scheduler, not a Windows
SCM service. The admin's source of truth includes process identity and runtime telemetry, not
only the scheduler's Running flag. Normal close leaves background processing alone.

For a continuation start with actual task/process/runtime state, unprinted configuration,
source diff and relevant tests. Avoid overwriting the user's tokens, queue, stop choice or
current publication mode. This historical context does not require recreating Telegram assets.

Current project's attribution policy: omit the donor Telegram post/channel link from outgoing text, but keep Source.Url internally for provenance/admin access and retain the external article's primary-source link when present. This is the user's project-specific preference, not a universal rule for other channels.

Archive support: the Admin Archive tab previews an inclusive Kyiv date range or the last N text posts, then explicitly enqueues selected unknown IDs. Scan never calls AI or publishes; queued posts use the normal relevance filter and daily budget. INSERT OR IGNORE treats all existing states as known and leaves the live cursor intact. SourcePost.PublishedUtc is optional for old JSON compatibility. Current .NET test count: 24.

Current UI: English by default, live EN/RU switch, preference in data/admin.preferences.json.
Content language remains Russian independently. Token accounting uses ai_usage and response.usage,
including incomplete/rejected results when usage was reported, summed across article revisions.
Old usage is unavailable and is not reconstructed. No daily token budget was chosen by the user.
The rate-window remainder is not a money balance. Current .NET test count: 31.
