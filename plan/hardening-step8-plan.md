# Prod-hardening (plan step 8) — phased implementation plan

**Source spec:** [`docs/hardening-prompt.md`](../docs/hardening-prompt.md) · **Status (2026-05-31):** ✅ **P1 + P2 DONE & deployed** · **P3 CODE-COMPLETE on branch `p3-durable-conversation-store` (103 tests green, NOT deployed)** · P4–P5 remaining.
**Context:** the bridge is LIVE in production (see [`RESUME.md`](../RESUME.md) → *Current state*). It was built
dev-grade on purpose; this plan hardens it. Built **P1-first** because P1 has a hard external deadline.

> **Progress:** **P1 (token auto-refresh + Key Vault persistence) and P2 (App Insights + structured logging)
> are deployed and verified in production** (deployed build `df706c8`). Test count is now **44** (was 20).
> Remaining: P3 (durable conversation store), P4 (attachments/stories), P5 (broader secret rotation).
> See [`RESUME.md`](../RESUME.md) → *Done 2026-05-30/31* for the live Azure resources, the root-cause writeup
> (missing managed-identity injection), and the deploy/diagnostic gotchas.

> **Build order:** **P1 standalone and committed first** (token-expiry deadline ~late July 2026), then P2 → P3 →
> P4 → P5 fold in. Every phase keeps `dotnet test` green and adds tests for new behaviour. Deploys happen only on
> Chris's explicit go-ahead.

---

## Guardrails (apply to every phase)

- **No real secrets in git.** `appsettings.json` stays placeholders; real values live in App Service app settings / Key Vault.
- **All tests pass at every commit.** Currently **20**; each phase adds tests.
- **Do NOT revert `InstagramClientWrapper` from `graph.instagram.com`** (outbound IG-Login requirement).
- **Zip-deploy uses forward-slash paths** (Python `zipfile` + `.replace('\\','/')`, not PS5 `Compress-Archive`).
  Deploy: `dotnet publish Microsoft.OmniChannel.Adaptors.Service -c Release -o publish` → zip → `az webapp deploy
  -n awd-ig-bridge -g awd-contactcenter-rg --src-path publish.zip --type zip`.
- `az` authed as `chris@chrismurray.eu`, sub "Core Benefits Credits"; remote `chrismurrayawd/awd-ig-bridge` → `main`.

---

## P1 — Instagram-user token auto-refresh ✅ DONE 2026-05-31 (was: HARD DEADLINE ~late July 2026 — now eliminated)

> **DONE & LIVE.** Implemented as designed below: background refresher + provider-fed outbound + durable Key
> Vault store via the App Service managed identity. Verified in prod (secret `IgUserAccessToken` in
> `awd-ig-bridge-kv`, expires 2026-07-29; TokenHealth `storeType=KeyVaultInstagramTokenStore, storeGet=ok,
> msiProbeStatus=200`). The original design notes below stand as the as-built record.

**Problem.** `InstagramAdapterSettings:PageAccessToken` is an Instagram-user token (`IGAA…`) that **expires ~60 days**
after generation (2026-05-30). The outbound Send API (`graph.instagram.com/{ver}/{IgBusinessId}/messages?access_token=…`)
reads this token from `IOptions<InstagramAdapterConfiguration>` **at send time** ([`InstagramClientWrapper.cs:82`](../Libraries/Adapters/Microsoft.OmniChannel.Adapters.Instagram/InstagramClientWrapper.cs#L82)).
When the token lapses, **outbound silently breaks** — agent replies never arrive, no error surfaced.

**Solution.** A background refresher slides the token forward indefinitely, persisting it durably (Key Vault) so a
restart never reverts to a stale value. The outbound sender reads the token from a **provider** (in-memory cache fed
by the store + refresher) instead of from `IOptions`, so a refresh takes effect with **no restart**.

### Refresh mechanics (Instagram API with Instagram Login)
- `GET https://graph.instagram.com/refresh_access_token?grant_type=ig_refresh_token&access_token={token}`
- Valid only when the token is **≥24 h old** and **unexpired**. Returns `{ access_token, token_type, expires_in }`
  (`expires_in` ≈ 5,184,000 s = ~60 days). Each successful refresh resets the ~60-day window → refreshing well before
  expiry keeps it alive forever (the App Service is Always-On).
- Error handling: code `190` (expired/invalid) → **terminal**, log CRITICAL (manual re-mint needed, can't auto-recover);
  "token must be ≥24 h old" → **transient/skip**, retry next cycle.

### Persistence — Key Vault + system-assigned managed identity (decided; ties into P5)
- Token secret stored in **Key Vault** `awd-ig-bridge-kv`; the App Service reads it at startup and the refresher writes
  the new value back. Token expiry tracked via the **KV secret's `ExpiresOn` attribute** (set on each write) — the
  source of truth for "remaining lifetime", no separate metadata store.
- **Auto-seed (Chris's call):** if `KeyVault:Uri` is configured but the secret is missing, the bridge seeds it on first
  startup from the current `InstagramAdapterSettings:PageAccessToken` app setting → zero manual step, deploy-order-proof.
  Once KV holds the token, **KV is authoritative forever** (the old app setting never clobbers a refreshed token).
  *Post-deploy cleanup:* once the seed is confirmed in KV, delete the `InstagramAdapterSettings__PageAccessToken` app
  setting so KV is the single source of truth.
- **Local dev / tests:** when `KeyVault:Uri` is empty, a **config-fallback store** is used (reads the config token;
  "set" updates in-memory only + logs that persistence is disabled). So build/test need **no Azure**.

### Code changes
New (in `Libraries/Adapters/Microsoft.OmniChannel.Adapters.Instagram/`):
- `TokenStore/IInstagramTokenStore.cs` + `InstagramTokenState` (record: `Token`, `ExpiresOn?`).
- `TokenStore/KeyVaultInstagramTokenStore.cs` — `SecretClient` + `DefaultAzureCredential`; get returns value + `ExpiresOn`
  (null when secret absent); set writes a new version with `ExpiresOn`. KV ops behind a thin `ISecretClientAdapter` seam
  so the store is unit-testable without a live vault.
- `TokenStore/ConfigInstagramTokenStore.cs` — fallback (config token; in-memory set).
- `TokenStore/IInstagramTokenProvider.cs` + `InstagramTokenProvider.cs` — singleton; thread-safe in-memory current token;
  `InitializeAsync()` (load from store, seed from config if empty), `GetTokenAsync()`, `SetAsync(state)` (update cache + store).
- `TokenRefresh/IInstagramTokenRefreshClient.cs` + `InstagramTokenRefreshClient.cs` — calls `refresh_access_token`, parses
  result, maps error codes to typed exceptions (`InstagramTokenExpiredException`, `InstagramTokenTooFreshException`).
- `TokenRefresh/InstagramTokenRefreshPolicy.cs` — pure `ShouldRefresh(state, now, thresholdDays)` (testable).
- `TokenRefresh/InstagramTokenRefreshService.cs : BackgroundService` — on start `InitializeAsync`; loop every
  `CheckIntervalHours`; if `ShouldRefresh` → refresh → `provider.SetAsync` → log; swallow exceptions so the host never crashes.

Changed:
- `InstagramClientWrapper` ctor takes `IInstagramTokenProvider`; reads the token from it at send time (AppSecret /
  IgBusinessId / GraphApiVersion still from `IOptions`). Drop the construction-time `PageAccessToken` non-empty throw.
- `InstagramAdapter` config-ctor takes + forwards `IInstagramTokenProvider` to the wrapper. Test-ctor `(InstagramClientWrapper)` unchanged.
- `Program.cs` — register store (KeyVault if `KeyVault:Uri` set, else Config), provider (singleton), refresh client,
  `AddHttpClient`, `AddHostedService<InstagramTokenRefreshService>()`.
- `appsettings.json` — add placeholders: `InstagramAdapterSettings:TokenSecretName` (`IgUserAccessToken`),
  `TokenRefreshThresholdDays` (`20`), `TokenRefreshCheckIntervalHours` (`12`); top-level `KeyVault:Uri` (`""`).
- Packages on the adapter project: `Azure.Security.KeyVault.Secrets`, `Azure.Identity` (net8, from nuget.org).

### Tests (xUnit + Moq, no Azure)
1. Refresh client: success parse (token + `ExpiresOn ≈ now+60d`); URL shape; `190` → expired exception; "24 h" → too-fresh exception.
2. `ShouldRefresh`: far-future → false; within threshold → true; unknown `ExpiresOn` → true.
3. Config store get/set round-trip.
4. Provider: seeds store from config when empty; returns store token when populated; reflects `SetAsync` (the no-restart path).
5. Refresh service/policy integration (fake provider + fake client): refresh-due+success → provider updated + logged;
   expired → CRITICAL log, provider unchanged, no throw; too-fresh → skip.
6. `InstagramClientWrapper` uses the provider's **current** token at send time (mock `HttpMessageHandler`): token "TKN1" →
   request carries TKN1; provider updated to "TKN2" → next send carries TKN2.
7. KeyVault store via fake `ISecretClientAdapter`: `ExpiresOn` round-trip; missing secret → null (→ provider seeds).

### Infra / deploy (Chris go-ahead required; az authed as chris@chrismurray.eu)
- Ensure system-assigned managed identity on `awd-ig-bridge` (`az webapp identity assign`).
- Create/confirm Key Vault `awd-ig-bridge-kv` (uksouth).
- **Grant the MI `Key Vault Secrets Officer`** (read **and** set — the refresher writes new versions; the deploy-notes'
  read-only `Key Vault Secrets User` is **insufficient** for P1). *(Correction to `docs/azure-deploy-notes.md` §3.)*
- App settings: `KeyVault__Uri=https://awd-ig-bridge-kv.vault.azure.net/`, `InstagramAdapterSettings__TokenSecretName=IgUserAccessToken`,
  threshold/interval. Keep `PageAccessToken` for the first-boot auto-seed; delete it after the seed is confirmed in KV.
- Deploy via the forward-slash zip incantation.

### Acceptance
A refresh produces a new token, **persists across an app restart** (startup reads the refreshed KV value, not the stale
app setting), outbound still works, and a refresh failure is **logged/alerted** (P2), never silent.

---

## P2 — Observability (Application Insights + structured logging) ✅ DONE 2026-05-31

> **DONE & LIVE.** App Insights `awd-ig-bridge-ai` wired (`APPLICATIONINSIGHTS_CONNECTION_STRING` set);
> AddApplicationInsightsTelemetry + logging provider; structured/queryable logs for webhook verify, signature
> pass/fail, inbound accept, and the previously-silent outbound-failure path (now ILogger.Error). The
> `/api/TokenHealth` diagnostic endpoint was also added. Notes below stand as the as-built record.
- Add `builder.Services.AddApplicationInsightsTelemetry()` + `APPLICATIONINSIGHTS_CONNECTION_STRING`.
- Structured, queryable logs for: inbound webhook received; **signature pass/fail** (today only the 403
  `LogWarning("postactivityasync rejected")`); payload→activity counts; relay→Direct Line result; **outbound Send-API
  request + response status**; **token-refresh events + ANY Send-API/token failure**.
- **Acceptance:** a signature failure and a full round-trip are both queryable in App Insights within ~1 min.

## P3 — Durable conversation store (replace in-memory cache + polling)  ◀ CODE-COMPLETE 2026-05-31 (branch, not deployed)

### ✅ AS-BUILT (2026-05-31) — code-complete on branch `p3-durable-conversation-store`, awaiting Chris's deploy go-ahead

Built per the locked design below, in 10 commit-sized steps (`dotnet test` green at every commit; **103 tests**, was
44), then an adversarial review (5 lenses → verify → synthesize, verdict **SHIP** after 3 fixes — all applied).
**Not merged, not deployed.** Branch off `main`.

**What shipped:** Line channel removed → `IConversationStore` + `InMemoryConversationStore` → `IDirectLineGateway`
seam → `RetryExecutor`/`TransientFaultClassifier` + `ResilientDirectLineGateway` + typed `InstagramSendException`
→ `IOutboundActivitySink`/`OutboundSinkResolver` + gutted stateless `RelayProcessor` (deleted `ActiveConversationCache`
+ `DirectLineConversation` + the polling `Thread`) → `ConversationPollingService` (rehydration loop) →
`TableConversationStore` + `ITableClientAdapter` → DI wiring + 3 smoke tests → adversarial-review fixes.
The headline acceptance is proven by a unit test (seed an Active row as if it survived a restart, run one poll tick,
the agent reply reaches the sink addressed to the IGSID — rehydration *is* the steady-state loop).

**▶ Deploy status — ✅ DEPLOYED & verified 2026-05-31 (PR [#1](https://github.com/chrismurrayawd/awd-ig-bridge/pull/1) merged `1ad6c7a`):**
1. **✅ Storage provisioned:** account **`awdigbridgestore`** (UK South, `awd-contactcenter-rg`, StorageV2/TLS1.2/
   no-public-blob); the App Service MI `b3429ddd-…` granted **`Storage Table Data Contributor`** on it (same MI as P1).
2. **✅ Activated + deployed:** app setting `RelayProcessorSettings__TableServiceUri=https://awdigbridgestore.table.core.windows.net/`
   set; Python-zipfile forward-slash deploy (the first `az webapp deploy` 502'd on SCM cold-start → a straight retry
   succeeded). The `Conversations` table **auto-created on boot** — proves the Table store is active (not the
   in-memory fallback) and the MI works; app healthy, **zero errors/criticals on startup** (query App Insights with
   `-o json`, not `-o table`).
3. **✅ #1 live acceptance — PASSED 2026-05-31 (real customer round-trip, Chris driving IG + D365):** real IG DM →
   durable row created → **App Service restarted mid-conversation** → post-restart poller rehydrated it (Active,
   `LastPolledOn` advanced) → the agent's D365 reply was relayed to IG and **landed in the customer's Instagram
   inbox**. So Direct Line DOES resume a conversation by `ConversationId`+watermark across the restart gap on the
   3.0.2 SDK (no 404/Faulted); a reply sent after a restart is never dropped. No DL-token persist needed. **P3
   acceptance COMPLETE. Keep the App Service at one B1 instance** (lease columns dormant).

**Accepted residuals (documented, not bugs — confirmed by the review):**
- **At-least-once**: a crash between the `LastDeliveredActivityId` write and the watermark write can re-deliver the
  non-last activities of a *multi-part* reply next tick (duplicate, never a drop). Single-activity replies are
  effectively-once. Full per-activity ledger deferred (Chris's call).
- **EndOfConversation close-race (TOCTOU, single-instance)**: an inbound read as Active can post onto a row the poller
  concurrently marks Ended, stranding that inbound until the customer's next message. Narrow window; the 404/Faulted
  sub-case is *not* silent (the post also 404s → throws to the webhook → Meta retries). Defensive fix deferred.
- **Single-instance assumption**: `OwnerLease`/`LeaseExpiry` columns exist but are dormant — **keep the B1 at one
  instance** until a lease CAS is wired (two pollers would double-poll/double-send). A defensive `ConversationId`
  guard in `UpdateRowAsync` was added as cheap scale-out insurance.

**Recommended test backfill before Live-mode customer traffic (not gating the Testers trial):** `_inFlight` overlap,
`UpdateRowAsync` ETag-conflict/exhaustion, a real Create→ListActive Table round-trip (integration), create-race at the
store level.

### 🔒 LOCKED DESIGN (2026-05-31) — chosen via a judged design workflow; design brief below is the source spec

Three candidate architectures were raced through a design workflow (3 architects → 3-lens judge panel →
synthesis). **Candidate A — "stateless relay + single-loop poller + DI-resolved sink" — won all three lenses**
(restart-correctness 9, concurrency/ops 8, testability 8). Chris locked it 2026-05-31 with two decisions.

**The architecture (mirrors the shipped P1 store/seam/BackgroundService patterns exactly):**
- **Sink (kills the un-persistable closure):** delete the per-call `EventHandler<IList<Activity>>`. New
  `IOutboundActivitySink` + `OutboundSinkResolver` delegate **declared in the relay project** (zero new project
  refs — respects the one-way `adapter→relay` edge), resolved by the row's persisted `ChannelType`.
  `InstagramAdapter` implements the sink by forwarding to its existing `ProcessOutboundActivitiesAsync`.
- **Poller:** ONE `ConversationPollingService : BackgroundService` (shaped like `InstagramTokenRefreshService`).
  Each tick lists Active rows and polls them with **bounded-parallel fan-out** (`SemaphoreSlim`) + a per-IGSID
  in-flight guard. **Rehydration IS the steady-state loop** — no separate restart-recovery code path. The raw
  `new Thread` and the static `ActiveConversationCache` are deleted.
- **Store:** `IConversationStore` + `TableConversationStore` (Azure.Data.Tables, managed identity — same MI as
  P1) + `InMemoryConversationStore` fallback (so build/tests need no Azure). Behind a thin `ITableClientAdapter`
  seam (the `ISecretClientAdapter` twin). PK=`ChannelType`, RK=`IGSID`. Columns: `ConversationId`, `WaterMark`,
  `Status` (Active|Ended|Faulted), `ChannelType`, `CreatedOn`, `LastPolledOn`, `LastInboundOrReplyOn` (the idle
  signal for the sweep), `LastDeliveredActivityId` (dedup guard), `OwnerLease`+`LeaseExpiry` (dormant; future
  scale-out), ETag.
- **Direct Line seam:** `IDirectLineClient` + `DirectLineActivitySet` DTO + `IDirectLineClientFactory` +
  `DirectLineClientAdapter` (wraps the SDK) + `ResilientDirectLineClient` retry decorator — poller testable with
  a `FakeDirectLineClient`, no live Direct Line.
- **Delivery ordering:** **deliver to Instagram FIRST, persist the watermark AFTER** (ETag CAS). Crash before
  delivery → re-delivered next tick (never lost). Crash after IG-200 before commit → `LastDeliveredActivityId`
  guard skips the re-send for the common single-activity reply.
- **Retry/backoff:** hand-rolled bounded exponential backoff (no Polly — matches the existing hand-rolled
  refresher). Transient (5xx/429/timeout) retries; terminal (401/403/400) logs **loud** via P2 App Insights and
  does NOT retry into silence. Needs a typed `InstagramSendException` (status no longer buried in a message
  string).
- **Stale cleanup:** TTL sweep inside the same poller on `LastInboundOrReplyOn`, default 48h (D365
  `msdyn_autocloseafterinactivity`).
- **Concurrency:** ETag optimistic concurrency; create-race resolved by insert-if-absent (**409**, correctly).
  Keep B1 **single-instance** until the dormant lease is wired.

**Chris's two decisions (2026-05-31):**
1. **Delivery guarantee = at-least-once + the `LastDeliveredActivityId` single-activity dedup guard** (a rare
   duplicate beats a silent drop; full per-activity ledger deferred).
2. **Remove the Line sample channel entirely** (project + controller + registrations) — the resolver becomes
   Instagram-only, rather than updating Line to the new contract.

**Two deploy-time items (do NOT block the code; InMemory + loud-Faulted fallback cover them):**
- **#1 step-8 live acceptance:** prove Direct Line resumes a conversation by `ConversationId`+watermark across a
  restart on the old `Bot.Connector.DirectLine 3.0.2` SDK. If it 404s, GetActivities marks the row **Faulted**
  and logs Critical (visible, never silent); a DL-token persist/refresh would be added only if the live test fails.
- **Provision** a Storage account (UK South) + grant the App Service MI **Storage Table Data Contributor** + set
  `RelayProcessorSettings:TableServiceUri` — the same easy-to-forget MI grant that bit P1.

**Build order (each commit keeps `dotnet test` green; baseline 44):** remove Line → test doubles → store +
in-memory → Direct Line seam → resilience + typed send exception → sink + gut RelayProcessor (new signature,
delete static cache/DirectLineConversation) → polling BackgroundService (+ headline rehydration test) →
Table store + mapping tests → wire Program.cs/config/packages → adversarial review → docs.

---

**Goal:** a process restart must not drop in-flight conversations. Today an agent reply that arrives after a
restart is **silently lost** — which is why re-testing outbound this whole project needed a *fresh* DM each time.

### What exists today (the as-built to replace)
- **`ActiveConversationCache`** — a `static ConcurrentDictionary<string, DirectLineConversation>` keyed by
  **IGSID** (`inboundActivity.From.Id`). Purely in-memory; empty after any restart.
- **`DirectLineConversation`** holds three things: a live **`DirectLineClient`** (Bot.Connector SDK, NOT
  serializable), the **`Conversation`** (its `ConversationId` is the durable bit), and a **`WaterMark`** string.
- **Polling** — `RelayProcessor.InitiateConversation` spins up a raw **`new Thread(... PollActivitiesFromBotAsync ...)`**
  per conversation. The loop calls `GetActivitiesAsync(conversationId, watermark)`, forwards activities where
  `from.id == BotHandle` to the adapter via the **`adapterCallBackHandler`** delegate, updates the in-memory
  watermark, and `return`s (ends the thread) on `EndOfConversation`.
- **The hard part:** `adapterCallBackHandler` is an `EventHandler<IList<Activity>>` **closure passed per
  `PostActivityAsync` call** (wired from `InstagramAdapter.OnActivitiesReceived`). It cannot be persisted, so on
  rehydration after a restart there is no inbound call to supply it — the outbound delivery path must instead be
  **resolvable from DI**, not captured per-call. This is the central redesign, more than the storage itself.

### Recommended approach (confirm in the P3 design workflow, don't assume)
- **Store: Azure Table Storage** (not Service Bus). We need a *keyed, updatable lookup* (IGSID → {ConversationId,
  WaterMark, state, timestamps}), which is exactly Table Storage's model; Service Bus is a queue, the wrong shape
  for "current state of conversation X". Cheap, in the existing sub, same managed-identity/DefaultAzureCredential
  pattern P1 already established (`Azure.Data.Tables` + the MI). Partition key = a constant or channel id; row key
  = IGSID. Columns: `ConversationId`, `WaterMark`, `CreatedOn`, `LastPolledOn`, `Status` (Active/Ended).
- **Polling: one `BackgroundService`** (like the P1 refresher), NOT a thread-per-conversation. On startup it
  **rehydrates** every Active row → recreates a `DirectLineClient` from the Direct Line secret + stored
  `ConversationId` and resumes polling from the stored `WaterMark`. New conversations add a row; the same loop
  picks them up. **Persist the watermark after every poll** so a restart resumes with zero/at-most-one-poll replay.
- **Outbound path via DI:** replace the per-call `adapterCallBackHandler` closure with an injectable interface
  (e.g. `IOutboundActivitySink` implemented by the Instagram adapter, resolved by channel). The relay/poller
  resolves the sink from the container, so rehydrated conversations can deliver replies with no inbound trigger.
  `RelayProcessor.SendReplyActivity` already only sets `ReplyToId = inbound From.Id` (= IGSID) + `ChannelId` — keep
  that contract; IGSID is the row key, so it's recoverable without the closure.
- **Retry/backoff** on transient Direct Line (`StartConversationAsync`, `GetActivitiesAsync`, `PostActivityAsync`)
  and IG Send API failures — e.g. Polly or a small hand-rolled exponential backoff; distinguish transient (5xx,
  429, timeout) from terminal (4xx auth) so a dead token still surfaces loudly (P2) rather than retrying forever.

### Files in play
- `Microsoft.OmniChannel.MessageRelayProcessor/RelayProcessor.cs` — the rewrite (remove thread-per-conv + static cache).
- `Microsoft.OmniChannel.MessageRelayProcessor/Utility/ActiveConversationCache.cs`, `DirectLineConversation.cs` —
  replace with a store abstraction (`IConversationStore` + `TableConversationStore` + a config/in-memory fallback
  for local/tests, mirroring P1's `IInstagramTokenStore` pattern).
- `RelayProcessorConfiguration.cs` — add the table/connection settings (+ reuse `KeyVault__Uri`/MI patterns).
- `Libraries/Adapters/.../InstagramAdapter.cs` — expose the outbound sink for DI resolution instead of the closure.
- `Microsoft.OmniChannel.Adaptors.Service/Program.cs` — register store, sink, and the polling `BackgroundService`.
- New tests under `Tests/...MessageRelayProcessor.Tests` (store round-trip, rehydration, watermark persistence,
  retry/backoff, EndOfConversation cleanup) — no Azure needed (fake `IConversationStore` + stub Direct Line client seam).

### Open design questions for the P3 session to resolve first
1. **Direct Line client seam** — `DirectLineClient`/`Conversations` is concrete SDK; wrap behind an interface so
   the poller is unit-testable without a live Direct Line (mirror P1's `ISecretClientAdapter` seam).
2. **Concurrency/idempotency** — Table ETag optimistic concurrency on watermark writes; ensure a single poller
   owns a conversation (single B1 instance today, but don't bake in the assumption — consider a lease/owner column).
3. **Stale conversation cleanup** — if D365 never emits `EndOfConversation`, rows linger. Add a TTL / max-idle
   sweep (ties to the `msdyn_autocloseafterinactivity` 48h in RESUME.md).
4. **Local/dev + tests** — config/in-memory `IConversationStore` when no table is configured (so build/tests need no Azure).

### Acceptance
Start a conversation (DM in), **restart the app**, then have an agent reply — the reply still reaches the customer
on Instagram (no dropped conversation, watermark resumed). Transient Direct Line/Send failures retry and recover;
a terminal/auth failure is logged loudly (P2), not retried into silence. `dotnet test` green with new P3 tests.

### Approach for the P3 session (ultracode)
Design-first: run a short **design workflow** (2–3 candidate architectures for the sink/rehydration problem,
judged) → lock the design → implement → **adversarial review** (concurrency, restart-correctness, retry semantics,
secret handling) like P1 → tests green → then deploy on Chris's go-ahead (forward-slash zip; verify via App
Insights / `/api/TokenHealth`-style health, since the log stream is dead — see [`RESUME.md`](../RESUME.md) gotchas).

## P4 — Richer attachment / story handling
- Today inbound media/stories degrade to a text placeholder (`InstagramHelper.DescribeAttachments`). Surface image/story
  URLs (or media) to the agent per the IG webhook payload shapes. Lower priority.

## P5 — Secret hygiene  ◀ CODE-COMPLETE on branch `p5-secret-hygiene` (NOT deployed; awaiting Chris's go-ahead)

### ✅ AS-BUILT (2026-05-31) — Option B (two parallel seams), design-first + adversarial review

Built per a judged design workflow (3 architects → 3-lens judge panel, all converged on **Option B**: two
parallel seams reusing the existing `ISecretClientAdapter` as-is, **zero P1-source churn**). Five commit-sized
steps, `dotnet test` green at every commit (**102 → 130**), then an adversarial review. **Not merged, not deployed.**

**What shipped:**
- **AppSecret (adapter project):** new `SecretStore/` trio mirroring P1 — `IAppSecretStore` +
  `KeyVaultAppSecretStore` (reuses `ISecretClientAdapter`, `expiresOn: null`) + `ConfigAppSecretStore` fallback +
  `IAppSecretProvider`/`AppSecretProvider` (singleton, lazy load, auto-seed from `AppSecret`, best-effort persist,
  no-cache-on-failed-init). `InstagramClientWrapper.ValidateSignature` → **async `ValidateSignatureAsync`** reads
  the provider (fail-closed → 403 on a missing secret, never throws); the empty-AppSecret ctor throw was dropped
  (provider owns it now; same precedent as P1's PageAccessToken). A backwards-compatible 3-arg wrapper ctor keeps
  the P1 Send-path tests compiling unchanged. `InstagramAdapterConfiguration.AppSecretName` added.
- **DirectLineSecret (relay + Service):** relay `IDirectLineSecretProvider { string GetSecret(); Task WarmAsync(ct); }`
  + `ConfigDirectLineSecretProvider` fallback. **`DirectLineGatewayFactory` ctor source swapped** from
  `IOptions<RelayProcessorConfiguration>` to the provider — the **only** relay-side change. KV-backed
  `KeyVaultDirectLineSecretProvider` lives in the **Service** composition root (the only project seeing both
  `ISecretClientAdapter` and the relay interface); load-once + auto-seed + best-effort persist; `GetSecret()`
  returns the cache with a defensive sync-block-once. `RelayProcessorConfiguration.DirectLineSecretName` added.
- **Init timing:** AppSecret lazy+cached (first webhook, async); DirectLine **warmed in `Program.Main` after
  `builder.Build()` / before `app.Run()`** so the eager gateway reads a hot cache synchronously. The
  config-fallback needs no warming → the no-Azure DI test still resolves `IDirectLineGateway`.
- **`/api/TokenHealth`** extended (length/type/boolean only, **never values**) to report both new secrets'
  store type + load result, for deploy verification given the unreliable log stream.
- **KV secret names:** `MetaAppSecret`, `DirectLineSecret` (P1's `IgUserAccessToken` unaffected). No background
  refresher (these don't expire). **Rotation runbook:** [`docs/secret-rotation-runbook.md`](../docs/secret-rotation-runbook.md).

**Deploy steps (Chris go-ahead; same MI/vault as P1):** deploy with the plaintext `AppSecret` +
`DirectLineSecret` app settings still present → bridge auto-seeds `MetaAppSecret` + `DirectLineSecret` into KV on
boot → verify via `/api/TokenHealth` (`*StoreHasValue: true`, correct types/lengths) + a real DM round-trip →
**rotate both** (Meta Reset / Direct Line regenerate → `az keyvault secret set` → restart → verify) → **delete the
plaintext app settings** (keep one verified cycle first, per the P1 playbook). Forward-slash zip; first deploy may
502 (retry); App Insights with `-o json`. Keep B1 single-instance.

---

### Original spec (the brief the build executed)

**Goal:** move the two remaining plaintext secrets — `InstagramAdapterSettings:AppSecret` (validates inbound
`X-Hub-Signature-256`) and `RelayProcessorSettings:DirectLineSecret` (Direct Line) — into Key Vault `awd-ig-bridge-kv`
(already exists from P1), served via the App Service managed identity, with a **config-fallback** so build/tests need
no Azure. The IG-user token is already in KV (P1). Then **rotate** the IG app secret + DirectLineSecret (both were
handled in plaintext on 2026-05-30) and document a rotation runbook.

### What P3/P1 already gave us (makes P5 small)
- **`ISecretClientAdapter` + `KeyVaultSecretClientAdapter`** (in the Instagram adapter project) already wrap KV via
  `DefaultAzureCredential`/the MI, with a 404→null seam and unit tests — **reuse this** (or lift it to a shared spot)
  rather than re-inventing. The MI `b3429ddd-…` already has secret get/set/list on `awd-ig-bridge-kv`.
- **`DirectLineSecret` has exactly ONE consumption point by design:** `DirectLineGatewayFactory` (it reads
  `IOptions<RelayProcessorConfiguration>.DirectLineSecret`). P3 built it this way specifically so P5 swaps only the
  factory's secret source to a KV-backed provider — **zero poller/relay change.**
- **`AppSecret` is read in `InstagramClientWrapper.ValidateSignature`** (`_configuration.Value.AppSecret`, per inbound
  webhook). Give it a provider seam (mirror `IInstagramTokenProvider`: load-once-at-startup, cache in memory).

### Recommended approach (confirm in a P5 design workflow, don't assume)
- A small **`IKeyVaultSecretProvider`** (or reuse the token-store pattern): when `KeyVault:Uri` is set, fetch
  `AppSecret` + `DirectLineSecret` from KV at startup (cache in memory; they don't auto-expire, so NO background
  refresher needed — unlike the IG-user token); when empty, **config-fallback** to the app settings (local/tests).
- **Auto-seed like P1:** if `KeyVault:Uri` is set but a secret is missing, seed it from the current app setting on
  first boot, then KV is authoritative; delete the plaintext app setting post-seed.
- **Rotation = update KV + restart** (these secrets don't expire, so no slide-forward refresher). Runbook: AppSecret →
  Meta app dashboard "Reset" → write new value to KV → restart; DirectLineSecret → Azure Bot → Direct Line channel
  "regenerate key" → write to KV → restart. A bad AppSecret breaks inbound (403); a bad DirectLineSecret breaks the
  relay/poller — so verify after each.
- **No secret in git/logs** (existing guardrail). KV secret names: e.g. `MetaAppSecret`, `DirectLineSecret`.

### Acceptance
Inbound still validates (signed webhook → 200) and outbound still works with both secrets served from KV (verified via
a real DM round-trip or the `Conversations`-table-created + inbound-200 signals); build/`dotnet test` green with no
Azure (config-fallback); a rotation of each secret is performed + verified; the plaintext `AppSecret`/`DirectLineSecret`
app settings are deleted once KV is authoritative.

### ▶ P5 kickoff prompt (paste into a fresh session)
> Continue the awd-ig-bridge hardening — do **P5 (secret hygiene)**. P1+P2+P3 are DONE & live (see `RESUME.md`).
> Read `RESUME.md` in full + `plan/hardening-step8-plan.md` → **P5** (the spec) + the P1 token-store files
> (`ISecretClientAdapter`/`KeyVaultSecretClientAdapter`/`IInstagramTokenProvider`) + `DirectLineGatewayFactory` +
> `InstagramClientWrapper`. **Ultracode/workflows:** design-first (a short design workflow for the secret-provider
> seam + auto-seed + rotation), lock the design with me, implement (keep `dotnet test` green per commit), then an
> adversarial review (no secret in git/logs, rotation doesn't break inbound/outbound, config-fallback, MI scope).
> Mirror P1's KV pattern + reuse `ISecretClientAdapter`. **Do NOT deploy without my go-ahead; pushes use SSH.**
> Deploy gotchas (don't relearn): GNU `tar` can't zip → Python `zipfile` forward-slash; first `az webapp deploy` may
> 502 on SCM cold-start → retry; `az monitor app-insights query -o table` renders empty → use `-o json`. Keep B1
> single-instance.

---

## Open coordination items for Chris
- **Key Vault provisioning + MI role** (P1 infra above) — needs your go-ahead; I can run the `az` commands.
- **Deploy of P1** — explicit go-ahead, then the forward-slash zip deploy.
- **Post-seed cleanup** — delete the `PageAccessToken` app setting once the KV seed is confirmed.
