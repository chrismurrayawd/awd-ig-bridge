# Azure deploy notes — IG bridge (plan step 4)

**Hosting decision RESOLVED → Linux App Service** (the code is an ASP.NET Core Web API; App Service
needs zero code change). Azure Functions is *not* used — see the footnote if that's ever revisited.

Chris drives Azure with his login (`chris@chrismurray.eu`, tenant Global Admin). Goal of step 4: host
the bridge, get a stable **public HTTPS URL** for the Meta webhook. Full message round-trip also needs
the **Direct Line secret** (D365 step 6) + the **Meta values** (step 5); until then the deployed app is
**GET-verify-testable only** (see §2.1).

The webhook path the app serves (both Meta's GET verify + POST events):

```
https://<webapp>.azurewebsites.net/api/InstagramAdapter/postactivityasync
```

---

## 0. Standard names (single source of truth)

| Thing | Value |
|---|---|
| Subscription | Core Benefits Credits `95b2f141-b4c6-4e9c-8d69-254c5be3baf9` ($2,400/yr credit, expires 2027-03-26) |
| Resource group | **`awd-contactcenter-rg`** (reuse the Contact Center RG — do **not** create a separate IG RG; keeps sprawl/credit-burn down) |
| Region | **UK South** (data residency; fallback West Europe) |
| App Service plan | `awd-ig-bridge-plan` (Linux, B1) |
| Web app | `awd-ig-bridge-<uniq>` (globally unique; **live name recorded in `RESUME.md` after deploy**) |
| Key Vault | `awd-ig-bridge-kv` (added when real secrets exist) |
| App Insights | `awd-ig-bridge-ai` |
| Sign in | `chris@chrismurray.eu` |

---

## 1. Local dev with ngrok (for the step-7 dev-mode test)

No `launchSettings.json`, so pin the port:

```powershell
# from repo root
$env:ASPNETCORE_URLS = "http://localhost:5280"
dotnet run --project Microsoft.OmniChannel.Adaptors.Service
```

Second terminal:

```powershell
ngrok http 5280
```

ngrok prints an HTTPS forwarding URL → your Meta webhook URL is `<that>/api/InstagramAdapter/postactivityasync`.
For real secrets locally, set env vars (double-underscore = config nesting) rather than editing
`appsettings.json`:

```powershell
$env:InstagramAdapterSettings__AppSecret      = "…"
$env:InstagramAdapterSettings__VerifyToken    = "…"   # must equal what you type into Meta's webhook UI
$env:InstagramAdapterSettings__PageAccessToken= "…"
$env:InstagramAdapterSettings__IgBusinessId   = "…"
$env:RelayProcessorSettings__DirectLineSecret = "…"
$env:RelayProcessorSettings__BotHandle        = "…"
```

(In Visual Studio: F5-debug the `Microsoft.OmniChannel.Adaptors.Service` project, set these under
Project → Debug → Environment variables, or `dotnet user-secrets`.)

---

## 2. Provision + deploy (App Service)

```powershell
az login
az account set --subscription "95b2f141-b4c6-4e9c-8d69-254c5be3baf9"

# Reuse the Contact Center RG (idempotent — confirms if it already exists)
az group create -n awd-contactcenter-rg -l uksouth

# Plan + web app (Linux, .NET 8)
az appservice plan create -n awd-ig-bridge-plan -g awd-contactcenter-rg -l uksouth --sku B1 --is-linux
az webapp create -n awd-ig-bridge-<uniq> -g awd-contactcenter-rg `
  --plan awd-ig-bridge-plan --runtime "DOTNETCORE:8.0"

# System-assigned managed identity (for Key Vault later)
az webapp identity assign -n awd-ig-bridge-<uniq> -g awd-contactcenter-rg
```

Deploy — **explicit publish + zip-deploy** (not `az webapp up`, and not `create` + `up` together):

```powershell
dotnet publish Microsoft.OmniChannel.Adaptors.Service -c Release -o publish
Compress-Archive -Path publish\* -DestinationPath publish.zip -Force
az webapp deploy -n awd-ig-bridge-<uniq> -g awd-contactcenter-rg --src-path publish.zip --type zip
```

> `az webapp deploy --type zip` expects a **zip file**, so the `Compress-Archive` step is required —
> pointing `--src-path` at the raw `publish` folder won't auto-zip.

Public host becomes `https://awd-ig-bridge-<uniq>.azurewebsites.net`.

### 2.1 App settings for the deploy-now / GET-verify-only phase

The D365 custom-channel step (which issues the Direct Line secret) is **blocked** — the Contact Center
environment is mid-reprovision after a trial→production conversion, so the secret can't be issued yet.
So for now:

- `InstagramAdapterSettings__VerifyToken` → set to a **real shared value now** (so Meta's webhook
  handshake verifies). It's a handshake nonce, not a credential. The live value is in `RESUME.md`.
- `InstagramAdapterSettings__AppSecret`, `InstagramAdapterSettings__PageAccessToken`,
  `InstagramAdapterSettings__IgBusinessId` → set when the Meta dedicated app exists (step 5).
- `RelayProcessorSettings__DirectLineSecret`, `RelayProcessorSettings__BotHandle` → **placeholders**
  until D365 step 6 issues them.

```powershell
az webapp config appsettings set -n awd-ig-bridge-<uniq> -g awd-contactcenter-rg --settings `
  "InstagramAdapterSettings__VerifyToken=<chosen-verify-token>" `
  "InstagramAdapterSettings__GraphApiVersion=v21.0" `
  "InstagramAdapterSettings__UseHumanAgentTag=false"
```

What works after this: **GET verify handshake** (Meta registration succeeds), signature rejection on
unsigned POSTs. What doesn't yet: the full DM round-trip (needs the Direct Line secret + real Meta token).

---

## 3. Secrets → Key Vault → app settings (when real secrets exist)

Once the Meta app + D365 channel issue real values, move them to Key Vault and reference them from app
settings (the app sees the resolved value; `__` = config nesting).

```powershell
az keyvault create -n awd-ig-bridge-kv -g awd-contactcenter-rg -l uksouth
az keyvault secret set --vault-name awd-ig-bridge-kv -n MetaAppSecret      --value "…"
az keyvault secret set --vault-name awd-ig-bridge-kv -n WebhookVerifyToken --value "…"
az keyvault secret set --vault-name awd-ig-bridge-kv -n IgPageAccessToken  --value "…"
az keyvault secret set --vault-name awd-ig-bridge-kv -n IgBusinessId       --value "…"
az keyvault secret set --vault-name awd-ig-bridge-kv -n DirectLineSecret   --value "…"
az keyvault secret set --vault-name awd-ig-bridge-kv -n BotHandle          --value "…"

# Grant the web app's managed identity read access
$kvId = az keyvault show -n awd-ig-bridge-kv -g awd-contactcenter-rg --query id -o tsv
$pid  = az webapp identity show -n awd-ig-bridge-<uniq> -g awd-contactcenter-rg --query principalId -o tsv
az role assignment create --assignee $pid --role "Key Vault Secrets User" --scope $kvId
```

| App setting (config key) | Value | Maps to |
|---|---|---|
| `InstagramAdapterSettings__AppSecret`       | `@Microsoft.KeyVault(SecretUri=https://awd-ig-bridge-kv.vault.azure.net/secrets/MetaAppSecret/)` | validates `X-Hub-Signature-256` |
| `InstagramAdapterSettings__VerifyToken`     | `@Microsoft.KeyVault(SecretUri=…/secrets/WebhookVerifyToken/)` | GET handshake |
| `InstagramAdapterSettings__PageAccessToken` | `@Microsoft.KeyVault(SecretUri=…/secrets/IgPageAccessToken/)`  | Send API |
| `InstagramAdapterSettings__IgBusinessId`    | `@Microsoft.KeyVault(SecretUri=…/secrets/IgBusinessId/)`       | `/{id}/messages` path |
| `InstagramAdapterSettings__GraphApiVersion` | `v21.0` (plain) | Graph version |
| `InstagramAdapterSettings__UseHumanAgentTag`| `false` (plain) | HUMAN_AGENT 7-day window |
| `RelayProcessorSettings__DirectLineSecret`  | `@Microsoft.KeyVault(SecretUri=…/secrets/DirectLineSecret/)`   | D365 custom channel (step 6) |
| `RelayProcessorSettings__BotHandle`         | `@Microsoft.KeyVault(SecretUri=…/secrets/BotHandle/)`          | Direct Line bot handle |
| `RelayProcessorSettings__PollingIntervalInMilliseconds` | `2000` (plain) | polling cadence |
| `APPLICATIONINSIGHTS_CONNECTION_STRING`     | from the App Insights resource | telemetry |

> `appsettings.json` in the repo holds **placeholders only** and stays that way. App settings override
> the JSON at runtime.

---

## 4. Verify the deploy

```powershell
# GET verify handshake — echoes the challenge if VerifyToken matches
curl "https://awd-ig-bridge-<uniq>.azurewebsites.net/api/InstagramAdapter/postactivityasync?hub.mode=subscribe&hub.verify_token=<chosen-verify-token>&hub.challenge=ping123"
# expect: ping123
# wrong token -> 403; unsigned POST -> 403; empty POST -> 400
```

Then register the URL on Meta → [`meta-webhook-registration.md`](meta-webhook-registration.md).

---

## Gotchas to carry in

- **24-hour service window** is stricter on IG than Messenger; set `UseHumanAgentTag=true` only if reps
  need to reply past 24h (extends to 7 days via the HUMAN_AGENT tag).
- **Token longevity** — prefer a **System User token** (non-expiring) for `PageAccessToken`; a dead
  token silently breaks outbound with no inbound symptom.
- **Dev-grade reliability** — in-memory conversation cache + polling thread; a restart drops active
  conversations. Fine for the Tester trial; plan step 8 hardens it.

---

> **Footnote — why not Azure Functions.** The fork is an ASP.NET Core Web API, which App Service hosts
> with no code change. Functions would need a port (wrap with the ASP.NET Core Functions integration,
> or an HTTP-triggered function fronting the controller). Not worth it for the trial; revisit only if
> there's a cost/scale reason at hardening (step 8).
