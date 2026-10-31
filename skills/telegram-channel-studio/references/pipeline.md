# Editorial pipeline decisions

## Source and audience

Identify donor, destination, language, reader experience and publication mode from the request
or existing configuration. Progress with sensible defaults when optional preferences are absent.
Do not confuse a donor title with its exact username. Check real content and access method.

A public `https://t.me/s/<username>` preview can provide text, code blocks and links without a
user session, but is not a stable Telegram API. Parse message boundaries by `data-post`, retain
line breaks/code, decode HTML entities, deduplicate IDs, and detect empty/broken extraction.
After downtime paginate rather than silently assuming the latest page contains all missed
messages. Bound catch-up; preserve the cursor when the missing interval cannot be resolved.
Media-only posts, deleted posts and unavailable history need an explicit support policy.

When the post is only an article teaser, either implement actual article retrieval with suitable
access/size limits or reject it as insufficient. Do not give the model only a URL and claim it
processed the full article. The existing project intentionally does not download linked articles.

## Useful .NET shape

- Core: source/draft models, publication validation.
- Infrastructure: source adapter, AI adapter, Telegram publishing, SQLite persistence, read-only dashboard queries.
- Worker: ingestion, state transitions, approval callbacks, bounded daily attempts, shutdown and runtime telemetry.
- Optional Windows launcher: hidden worker, log redirection, scheduler integration.
- Optional WPF admin: actual process status, read-only queue/details, logs, channel metadata, lifecycle controls.

This is a reference shape, not a mandate to create all projects for every small task. Reuse the
existing solution where present. Avoid adding queues/microservices merely for a single channel.

SQLite originals and metadata survive restarts. A unique source identity must include the
channel if multiple donors share one database; current DotNetChannel uses numeric message IDs
with exactly one donor per data directory. Do not add a second donor without addressing that key.

## Publication state machine

Existing states:

`Pending -> Processing -> Ready -> Notifying -> Review -> Publishing -> Published`

Automatic mode sends `Ready -> Publishing -> Published`.
Non-relevant materials become `Skipped`; generation failures become `Failed`.
Interrupted/ambiguous sends become `Uncertain`; do not automatically replay them.
On startup interrupted processing is recoverable as `Failed`, not a paid infinite retry.

Approval callback data includes action, source ID and draft revision. Check both the owner's
user ID and private chat, then atomically claim the matching state+revision. Preserve the
destination message ID when publication succeeds. Durable callback offsets reduce repeated
actions, but do not replace state claims or uncertainty handling.

In Telegram JSON omit optional `reply_markup` when absent. Sending `reply_markup: null` caused
an actual `Bad Request: object expected as reply markup`. Keep a regression test for omission.
Telegram code entity offsets count UTF-16 units; test non-BMP characters before code blocks.

## Editing through AI

Use an editable prompt plus a structured result (`relevant`, `text`, `reviewNotes`) when the
provider supports it. Reject incomplete responses and oversize output before publication.
Choose and verify a currently available text model; speech transcription models from other
projects are not suitable substitutes. Model IDs and prices are configuration, not eternal facts.

The current Russian .NET audience knows basic C# but wants clear explanations of advanced
topics. Prefer a plain heading, a practical scenario, one central insight, short accurate code
or a comparison of outputs, constraints, and source links. Avoid compiler terminology unless
it is needed. Distinguish parameters, instance fields, properties and captured variables.

The first real example exposed a subtle error: both ordinary and primary constructors had
`check()=3`; the difference was the virtual property reading `null` versus `3`. The existing
`FirstArticleExampleTests` verifies this. Do not generalize a difference in one output to all
outputs. A manual editorial check found a longer, less accurate draft despite a successful API
response; GPT-5.2 plus clearer instructions performed better on this sample than GPT-5 Mini.
That observation is a project result, not a universal model ranking.

## Operational information

Show process existence separately from application/source health. Report last successful
source read, last error, heartbeat, queue states and actual daily attempts. If showing channel
subscribers from `getChatMemberCount`, include bots and show the time of the network query.
Do not invent reach, views, cost or engagement metrics that were never collected.

Official references to check when implementing current APIs:

- https://core.telegram.org/bots/api
- https://core.telegram.org/bots/tutorial
- https://core.telegram.org/api/obtaining_api_id (only if a user client is actually required)
- https://developers.openai.com/api/docs/guides/text
- https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/

## User-selected history scanning

Offer an explicit preview for a local-calendar date range (convert inclusive end day to an
exclusive UTC bound) or the last N text posts. Keep a bounded page limit, cancellation and honest
partial-result warnings. Read pagination metadata even for media-only pages. Missing dates must
not silently enter a date-range selection. Preview should not invoke AI or publish anything.

On explicit enqueue, use a unique-ID insert rather than a read-then-write check. Every persisted
state counts as already known, including skipped ads, failures and pending work. Intentional
retry is a separate action. History enqueue must not move the live ingestion cursor. The normal
AI relevance filter then rejects ads, vacancies and insufficient teasers within the existing
attempt budget. Preserve a stopped service; explain that queued history waits for start/budget.

## Localization and token accounting

Keep UI language independent from content language. Persist the user's UI choice and route both
static controls and dynamic statuses/validation through a catalog. Provider messages and article
content are data, not necessarily translated UI. Check a live switch and both rendered layouts.

For token usage, begin a durable attempt record before the AI call and persist response.usage
before rejecting incomplete or invalid output. Aggregate all captured attempts/revisions per article.
Cached input and reasoning output are already subsets of input/output; do not double-count them.
A retry or unknown response must not turn missing measurements into zero. Historical calls made
before instrumentation cannot be recovered just by counting the saved text; label them unknown.

Distinguish app daily request limits, an optional user-defined token budget, provider rate-window
remaining tokens and the account's monetary credit. Show timestamps on rate-window snapshots.
Never describe x-ratelimit-remaining-tokens as prepaid account balance. Use mocked usage responses
for accounting tests instead of making extra paid calls or republishing old articles for statistics.
