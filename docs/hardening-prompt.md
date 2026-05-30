# Hardening prompt — Instagram → D365 bridge (awd-ig-bridge)

> Paste this into a fresh Claude Code session opened in `C:\GIT\awd-ig-bridge` to execute the deferred
> prod-hardening (plan **step 8**). Or run `/breakdown-spec docs/hardening-prompt.md` → `/implement`.
> **Read `RESUME.md` and `plan/instagram-direct-line-bridge-plan.md` first.**

## Context — what exists / what's done
The bridge (Azure Linux App Service `awd-ig-bridge`, RG `awd-contactcenter-rg`, sub `95b2f141-b4c6-4e9c-8d69-254c5be3baf9`)
relays Instagram DMs ↔ Microsoft Dynamics 365 Contact Center. **As of 2026-05-30 it is LIVE and
production-ready**: inbound (IG webhook → X-Hub-Signature-256 validate → Direct Line → Azure Bot → D365
conversation) and outbound (D365 agent reply → Direct Line watermark poll → IG Send API on
graph.instagram.com) both work for **real (non-Tester) customers under Standard Access** — no App Review /
Tech Provider needed. It was built **dev-grade on purpose**; this task is the deferred hardening.

**Current Azure app settings (on `awd-ig-bridge`):**
- `InstagramAdapterSettings__AppSecret` = the **Instagram app secret** (app `1493427132185394`) — validates inbound signatures.
- `InstagramAdapterSettings__PageAccessToken` = an **Instagram-user token (`IGAA…`, ~60-day)** — used for the Send API on graph.instagram.com.
- `InstagramAdapterSettings__IgBusinessId` = `17841440469975661`; plus `__VerifyToken`, `__GraphApiVersion`=`v21.0`.
- `RelayProcessorSettings__DirectLineSecret` + `__BotHandle`=`awd-instagram-bot`.

## Priorities (do in this order)

### P1 — IG-user token auto-refresh  ⏰ HARD DEADLINE ~late July 2026
The `PageAccessToken` is an Instagram-user token that **expires ~60 days** after generation (generated
2026-05-30). When it lapses, **outbound silently breaks** (the "dead token breaks outbound silently"
code-review finding) — agents' replies just never arrive, with no error surfaced.
- Implement a background refresher: `GET https://graph.instagram.com/refresh_access_token?grant_type=ig_refresh_token&access_token={token}`
  (valid when the token is ≥24h old and unexpired) → returns a fresh ~60-day token. Run it well before
  expiry (e.g. daily; refresh if remaining lifetime < ~10 days). Use `debug_token` or track issued-at to know remaining life.
- **Persistence is the hard part.** The token lives in an app setting; an in-process refresh updates memory
  but a restart reverts to the stale value. Recommended: give the App Service a **system-assigned managed
  identity**, store the token in **Azure Key Vault**, have the bridge read it at startup and the refresher
  write the new value back to Key Vault. (Alt: managed identity with rights to PATCH its own app setting.)
- **Acceptance:** a refresh produces a new token, persists across an app restart, and outbound still works; a refresh failure is logged/alerted (see P2), not silent.

### P2 — Observability (Application Insights + structured logging)
Diagnosing the 2026-05-30 issues required Azure-metric archaeology because the app's `LogWarning`s never
reached a readable sink (`az webapp log tail` stayed empty all session).
- Add **Application Insights** (`builder.Services.AddApplicationInsightsTelemetry()` + connection-string app setting).
- Emit structured logs for: inbound webhook received; **signature pass/fail** (today only the 403
  `LogWarning("postactivityasync rejected")` — make it queryable); payload→activity counts; relay→Direct
  Line result; **outbound Send-API request + response status**; and **token-refresh events + ANY Send-API/
  token failure** so outbound never fails silently again.
- **Acceptance:** a signature failure and a full round-trip are both visible/queryable in App Insights within ~1 min.

### P3 — Durable conversation store (replace in-memory cache + polling)
The relay uses an in-memory `ActiveConversationCache` + a polling thread for the Direct Line watermark. A
**restart drops all in-flight conversations** — that's why a *fresh* DM was needed to re-test outbound after
each deploy this session.
- Replace the in-memory dict + watermark with a durable store (Azure **Table Storage** or **Service Bus**),
  keyed by IG/Direct Line conversation id + watermark, so restarts don't lose active conversations.
- Add **retry/backoff** on transient Direct Line + Send API failures.
- **Acceptance:** start a conversation, restart the app, confirm an agent reply still reaches the customer (no dropped conversation).

### P4 — Richer attachment / story handling
Inbound media/stories/mentions currently degrade to a text placeholder (`InstagramHelper.DescribeAttachments`).
Surface image/story URLs (or media) to the agent per the IG webhook payload shapes. Lower priority.

### P5 — Secret hygiene
The Instagram app secret + IG-user token were handled in plaintext during the 2026-05-30 session. Consider
**rotating** both (the IG app secret has a "Reset" button on the "API setup with Instagram login" page;
regenerate the token via step 2 "Generate access tokens") and moving all secrets into **Key Vault** (ties into P1).

## Constraints / gotchas learned 2026-05-30 (don't relearn these)
- **Zip-deploy must use forward-slash paths.** PowerShell 5.1 `Compress-Archive` writes backslashes → Linux
  App Service rsync fails on `wwwroot\default.htm`. Build the zip with Python `zipfile` + `.replace('\\','/')`
  (or PS7/`tar`). Deploy: `dotnet publish Microsoft.OmniChannel.Adaptors.Service -c Release -o publish` →
  zip → `az webapp deploy -n awd-ig-bridge -g awd-contactcenter-rg --src-path publish.zip --type zip`.
- **App-settings changes trigger a restart**; the worker may serve OLD config for 1–3 min before the new worker is up.
- **Inbound is IG-Login** (graph.instagram.com semantics; signed with the **Instagram app secret**).
  **Outbound is graph.instagram.com + IG-user token** — NOT graph.facebook.com (that returns "(#3) Application
  does not have the capability"). Do not revert `InstagramClientWrapper.GraphApiBaseUrl`.
- Keep the build green (`dotnet test` = 20 tests). Add tests for the new refresher + durable store.
- `az` is authenticated as `chris@chrismurray.eu` (sub "Core Benefits Credits"). GitHub remote
  `chrismurrayawd/awd-ig-bridge`; the repo commits directly to `main`.

## Suggested workflow
`/breakdown-spec docs/hardening-prompt.md` → phased plan under `plan/` → `/implement`. Given the deadline,
**P1 (token refresh) can be done standalone first.**
