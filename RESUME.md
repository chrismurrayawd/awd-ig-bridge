# RESUME — IG → D365 Contact Center bridge

**Parked 2026-05-23.** Plan code steps **1–3 are done**; the remaining steps are portal/credential
work Chris drives in a focused session. Pick up here.

## Where the code is

| | |
|---|---|
| Branch | `main` |
| HEAD commit | **`e096540`** (`e096540505543cc4566720732bc32275cb183ab7`) — "Step 2-3: replace MessageBird adapter with Instagram adapter + config wiring" |
| Baseline commit | `89219f8` — "Baseline: fork MS 'bring your own channel' sample, migrated to net8.0" |
| Build | `dotnet build` clean (0 warnings/errors); `dotnet test` = **20 passing** |
| Webhook path | `/api/InstagramAdapter/postactivityasync` (GET = Meta verify, POST = events) |

## Done — steps 1–3 (code, autonomous)

1. **Fork + build.** Microsoft "bring your own channel" sample (upstream `72a742a`) vendored at repo
   root, migrated **netcoreapp2.1/2.2 → net8.0** (host rewritten to minimal hosting). Relay processor /
   Direct Line plumbing / watermark polling unchanged from the sample.
2. **Instagram adapter** (`Libraries/Adapters/Microsoft.OmniChannel.Adapters.Instagram`): inbound
   `X-Hub-Signature-256` verify (HMAC-SHA256 of raw body) + payload→`Activity` map
   (`channelData.channelType="Instagram"`, IGSID as `From.Id`, echoes/receipts skipped, media/stories
   degrade to a text placeholder); outbound `Activity`→IG **Send API**; **GET webhook-verify** endpoint
   (`hub.challenge`). MessageBird adapter/controller/tests removed; LINE sample adapter left intact.
3. **Config wiring.** `InstagramAdapterSettings` + `RelayProcessorSettings` bound in `Program.cs`;
   `appsettings.json` holds **placeholders only**. Verified live: GET verify echoes challenge,
   wrong-token 403, unsigned POST 403, empty 400, correctly-signed POST 200.

**Scope = dev-grade** (Chris's call 2026-05-23): in-memory conversation cache + polling thread, single
config token, no retries/refresh. Hardening is plan **step 8** (durable store + retry/backoff + token
refresh + richer attachment/story handling) — deferred.

## Next — steps 4–7, 9 (portal/credential, Chris's logins)

Two prep docs are committed to make these faster:
- **Step 4 — Azure deploy:** [`docs/azure-deploy-notes.md`](docs/azure-deploy-notes.md) — ngrok for the
  dev test, resource provisioning, and the **secrets → Key Vault → app-settings** mapping table.
- **Step 5 — Meta webhook:** [`docs/meta-webhook-registration.md`](docs/meta-webhook-registration.md) —
  dedicated app, IG product + permissions, token, webhook + `messages` subscription, Testers.

| Step | What | Owner |
|---|---|---|
| 4 | Deploy to Azure; get public webhook URL; wire Key Vault | Chris |
| 5 | Meta: dedicated app, webhook + IG perms + Testers | Chris |
| 6 | D365: custom Direct Line channel + workstream → human queue (yields the **Direct Line secret** for `RelayProcessorSettings`) | Chris |
| 7 | Dev-mode test: Tester DMs the IG account → lands in agent workspace, reply returns to IG | Chris + Lachy |
| 8 | Prod hardening (durable state, retries, token refresh) | Claude Code (later) |
| 9 | App Review (Live mode) — **after FB review clears** | Chris |

**No rush on step 9:** IG Live-mode App Review must wait for the in-flight Facebook `pages_messaging`
review regardless (Meta reviews one submission per app at a time). Dev-mode testing (steps 4–7) needs
no App Review.

## Decisions

- ✅ **Dedicated Meta app for IG — NOT the FB Messenger app `27031932926443539`.** Keeps IG's App
  Review clock independent of the in-flight FB review (Meta does whole-app review, one submission at a
  time, irreversible once submitted). Confirmed 2026-05-23.
- ✅ **Dev-grade now, harden later** (step 8). Confirmed 2026-05-23.
- ✅ **Human-first routing** by default for the D365 workstream (mirror WhatsApp/FB); not bridge code.
- ⏳ **OPEN — Web API vs Azure Function host.** The code as built is an **ASP.NET Core Web API**
  (net8.0), but CLAUDE.md/README describe an *Azure Function*. App Service hosting needs **zero code
  change**; Functions hosting is a code port. **Settle this before the Meta webhook wiring** so the
  public URL shape is final. (Recommendation: App Service for the trial; revisit Functions at step 8 if
  wanted.) See `docs/azure-deploy-notes.md` §2.

## Build/run quickstart

```powershell
dotnet build Microsoft.OmniChannel.Connector.Sample.sln
dotnet test  Microsoft.OmniChannel.Connector.Sample.sln
$env:ASPNETCORE_URLS = "http://localhost:5280"
dotnet run --project Microsoft.OmniChannel.Adaptors.Service
# then: ngrok http 5280
```

Real secrets via env vars (`InstagramAdapterSettings__AppSecret=…`) or `dotnet user-secrets` locally;
Key Vault in Azure. **Never commit real secrets** — `appsettings.json` stays placeholders.

> Environment note: this machine had **no .NET SDK** (installed .NET 8 via winget) and an **empty
> NuGet feed** (repo-local `NuGet.config` restores nuget.org). Both already handled in-repo.
