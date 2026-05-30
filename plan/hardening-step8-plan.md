# Prod-hardening (plan step 8) — phased implementation plan

**Source spec:** [`docs/hardening-prompt.md`](../docs/hardening-prompt.md) · **Status (2026-05-31):** ✅ **P1 + P2 DONE & deployed** · P3–P5 remaining.
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

## P3 — Durable conversation store (replace in-memory cache + polling)
- Replace `ActiveConversationCache` (static `ConcurrentDictionary`) + the per-conversation polling `Thread`
  ([`RelayProcessor.cs`](../Microsoft.OmniChannel.MessageRelayProcessor/RelayProcessor.cs)) with a durable store
  (Azure **Table Storage** or **Service Bus**) keyed by IGSID / Direct Line conversation id + watermark, so restarts
  don't drop in-flight conversations. Add **retry/backoff** on transient Direct Line + Send API failures.
- **Acceptance:** start a conversation, restart the app, an agent reply still reaches the customer.

## P4 — Richer attachment / story handling
- Today inbound media/stories degrade to a text placeholder (`InstagramHelper.DescribeAttachments`). Surface image/story
  URLs (or media) to the agent per the IG webhook payload shapes. Lower priority.

## P5 — Secret hygiene
- Rotate the IG app secret + IG-user token (both handled in plaintext on 2026-05-30) and move **all** secrets
  (`AppSecret`, `DirectLineSecret`, token) into Key Vault — builds on P1's vault + managed identity.

---

## Open coordination items for Chris
- **Key Vault provisioning + MI role** (P1 infra above) — needs your go-ahead; I can run the `az` commands.
- **Deploy of P1** — explicit go-ahead, then the forward-slash zip deploy.
- **Post-seed cleanup** — delete the `PageAccessToken` app setting once the KV seed is confirmed.
