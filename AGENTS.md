# DotNetChannel project onboarding

Read `AGENT_HANDOFF.md` before continuing this project. For creating similar Telegram channels,
operating the pipeline, or migrating it, use the project-local skill:
`skills/telegram-channel-studio/SKILL.md`.

`PORTABILITY.md` explains transfer to a different computer or agent login. `ADMIN.md` and
`OPERATIONS.md` describe actual operation. The skill copy inside this project is canonical;
keep any installed copy in sync when changing it.

Do not print or commit `settings.local.json`, API keys, bot tokens or environment values.
Keep `data/channel.db` and publication IDs for continuation. Do not start a second worker
with the same bot, replay uncertain sends, or recreate existing Telegram resources by default.
Check actual running state and preserve a manual stop during unrelated work.

Build: `dotnet build DotNetChannel.sln -c Release` (Windows, .NET 10).
Tests: `dotnet test tests/Channel.Tests/Channel.Tests.csproj -c Release`.
Stop the running task before overwriting locked worker/admin binaries, then restore its prior
running state only when the current task calls for it. Closing the admin is not a worker stop.

Skill transfer helper tests: python -m unittest discover -s tests -p test_skill_transfer.py
