# Azure deploy notes — IG bridge (plan step 4)

**Prep doc — not yet executed.** Chris drives this with his Azure login (`chris@chrismurray.eu`,
tenant Global Admin). Goal of step 4: host the bridge on Azure, get a stable **public HTTPS URL** for
the Meta webhook, and serve secrets from Key Vault. Everything here is doable in one focused sitting
once you have the Direct Line secret (needs D365 step 6) and the Meta values (step 5).

> **Note the open decision first:** the code as built is an **ASP.NET Core Web API** (net8.0), not an
> Azure Function — even though CLAUDE.md/README call it a "Function". Two clean hosting options below;
> the App Service path needs **zero code change**. Decide before provisioning. See `RESUME.md`.

---

## 0. Target (from CLAUDE.md / plan)

| Thing | Value |
|---|---|
| Subscription | Core Benefits Credits `95b2f141-…` ($2,400/yr credit, expires 2027-03-26) |
| Resource group | `awd-contactcenter-rg` |
| Region | **UK South** (data residency; fallback West Europe) |
| Cost | small App Service/Function + Key Vault + App Insights ≈ a few £/mo, inside the credit |
| Sign in | `chris@chrismurray.eu` |

The webhook path the app serves (both Meta's GET verify + POST events):

```
https://<your-host>/api/InstagramAdapter/postactivityasync
```

---

## 1. Local dev with ngrok (do this for the step-7 dev-mode test, before any Azure deploy)

The app has no `launchSettings.json`, so pick the port explicitly:

```powershell
# from repo root
$env:ASPNETCORE_URLS = "http://localhost:5280"
dotnet run --project Microsoft.OmniChannel.Adaptors.Service
```

In a second terminal, expose it:

```powershell
ngrok http 5280
```

ngrok prints an HTTPS forwarding URL like `https://ab12-…-cd34.ngrok-free.app`. Your webhook URL for
Meta (step 5) is then:

```
https://ab12-…-cd34.ngrok-free.app/api/InstagramAdapter/postactivityasync
```

Kestrel on net8 doesn't need the old `-host-header` rewrite the sample README mentioned. For real
secrets locally, set them as env vars (double-underscore = config nesting) rather than editing
`appsettings.json`:

```powershell
$env:InstagramAdapterSettings__AppSecret      = "…"
$env:InstagramAdapterSettings__VerifyToken    = "…"   # must equal what you type into Meta's webhook UI
$env:InstagramAdapterSettings__PageAccessToken= "…"
$env:InstagramAdapterSettings__IgBusinessId   = "…"
$env:RelayProcessorSettings__DirectLineSecret = "…"
$env:RelayProcessorSettings__BotHandle        = "…"
```

(In Visual Studio, F5-debug the `Microsoft.OmniChannel.Adaptors.Service` project and set these under
Project → Debug → Environment variables, or use `dotnet user-secrets`.)

---

## 2. Provision Azure resources

Either the Portal or the CLI below (adjust names). Same tenant as the D365 environment.

```powershell
az account set --subscription "95b2f141-…"
az group create -n awd-contactcenter-rg -l uksouth

# Key Vault for secrets
az keyvault create -n awd-igbridge-kv -g awd-contactcenter-rg -l uksouth

# Application Insights (observability)
az monitor app-insights component create `
  --app awd-igbridge-ai -g awd-contactcenter-rg -l uksouth --application-type web
```

Then create the host — pick **A** (Web API, zero code change) or **B** (Functions, needs a port).

### Option A — App Service (matches what's built; recommended for the trial)

```powershell
az appservice plan create -n awd-igbridge-plan -g awd-contactcenter-rg -l uksouth --sku B1 --is-linux
az webapp create -n awd-igbridge -g awd-contactcenter-rg `
  --plan awd-igbridge-plan --runtime "DOTNETCORE:8.0"
# enable a system-assigned managed identity for Key Vault access
az webapp identity assign -n awd-igbridge -g awd-contactcenter-rg
```

Publish from the repo (or right-click → Publish in Visual Studio Professional):

```powershell
dotnet publish Microsoft.OmniChannel.Adaptors.Service -c Release -o publish
Compress-Archive -Path publish\* -DestinationPath publish.zip -Force
az webapp deploy -n awd-igbridge -g awd-contactcenter-rg --src-path publish.zip --type zip
```

Public host becomes `https://awd-igbridge.azurewebsites.net`.

### Option B — Azure Functions (only if we decide to port)

The current code is not a Functions project. To go this route you'd either (a) wrap the host with the
ASP.NET Core Functions integration / `FunctionsStartup`, or (b) front it with an HTTP-triggered
function that forwards to the controller logic. **This is a code task, not a config one — flag it back
to Claude Code before doing the Meta wiring** so the webhook URL shape is settled first.

---

## 3. Secrets → Key Vault → app settings mapping

Put each secret in Key Vault, then reference it from the app's configuration. App Service / Functions
expose app settings to .NET config with the **same `__` nesting** as the env vars above.

Store the secrets:

```powershell
az keyvault secret set --vault-name awd-igbridge-kv -n MetaAppSecret      --value "…"
az keyvault secret set --vault-name awd-igbridge-kv -n WebhookVerifyToken --value "…"
az keyvault secret set --vault-name awd-igbridge-kv -n IgPageAccessToken  --value "…"
az keyvault secret set --vault-name awd-igbridge-kv -n IgBusinessId       --value "…"
az keyvault secret set --vault-name awd-igbridge-kv -n DirectLineSecret   --value "…"
az keyvault secret set --vault-name awd-igbridge-kv -n BotHandle          --value "…"
```

Grant the app's managed identity read access (RBAC or access policy):

```powershell
$kvId = az keyvault show -n awd-igbridge-kv -g awd-contactcenter-rg --query id -o tsv
$appPrincipalId = az webapp identity show -n awd-igbridge -g awd-contactcenter-rg --query principalId -o tsv
az role assignment create --assignee $appPrincipalId --role "Key Vault Secrets User" --scope $kvId
```

Then wire app settings to **Key Vault references** (the app sees the resolved secret value):

| App setting (config key) | Value (Key Vault reference) | Maps to |
|---|---|---|
| `InstagramAdapterSettings__AppSecret`       | `@Microsoft.KeyVault(SecretUri=https://awd-igbridge-kv.vault.azure.net/secrets/MetaAppSecret/)`      | `InstagramAdapterConfiguration.AppSecret` |
| `InstagramAdapterSettings__VerifyToken`     | `@Microsoft.KeyVault(SecretUri=…/secrets/WebhookVerifyToken/)`  | `…VerifyToken` |
| `InstagramAdapterSettings__PageAccessToken` | `@Microsoft.KeyVault(SecretUri=…/secrets/IgPageAccessToken/)`   | `…PageAccessToken` |
| `InstagramAdapterSettings__IgBusinessId`    | `@Microsoft.KeyVault(SecretUri=…/secrets/IgBusinessId/)`        | `…IgBusinessId` |
| `InstagramAdapterSettings__GraphApiVersion` | `v21.0` (plain value, not a secret) | `…GraphApiVersion` |
| `InstagramAdapterSettings__UseHumanAgentTag`| `false` (plain) | `…UseHumanAgentTag` |
| `RelayProcessorSettings__DirectLineSecret`  | `@Microsoft.KeyVault(SecretUri=…/secrets/DirectLineSecret/)`    | from the D365 custom channel (step 6) |
| `RelayProcessorSettings__BotHandle`         | `@Microsoft.KeyVault(SecretUri=…/secrets/BotHandle/)`           | Direct Line bot handle |
| `RelayProcessorSettings__PollingIntervalInMilliseconds` | `2000` (plain) | polling cadence |
| `APPLICATIONINSIGHTS_CONNECTION_STRING`     | from the App Insights resource | logging/telemetry |

Set them, e.g.:

```powershell
az webapp config appsettings set -n awd-igbridge -g awd-contactcenter-rg --settings `
  "InstagramAdapterSettings__AppSecret=@Microsoft.KeyVault(SecretUri=https://awd-igbridge-kv.vault.azure.net/secrets/MetaAppSecret/)" `
  "InstagramAdapterSettings__VerifyToken=@Microsoft.KeyVault(SecretUri=https://awd-igbridge-kv.vault.azure.net/secrets/WebhookVerifyToken/)" `
  "RelayProcessorSettings__DirectLineSecret=@Microsoft.KeyVault(SecretUri=https://awd-igbridge-kv.vault.azure.net/secrets/DirectLineSecret/)" `
  # …rest of the table…
```

> `appsettings.json` in the repo holds **placeholders only** and stays that way — never commit real
> secrets. App settings override the JSON at runtime.

---

## 4. Verify the deploy (before handing the URL to Meta)

```powershell
# GET verify handshake — should echo the challenge if VerifyToken matches
curl "https://awd-igbridge.azurewebsites.net/api/InstagramAdapter/postactivityasync?hub.mode=subscribe&hub.verify_token=<your-verify-token>&hub.challenge=ping123"
# expect: ping123

# wrong token -> 403; unsigned POST -> 403; empty POST -> 400 (same as the local smoke test)
```

Once GET verify echoes the challenge against the live host, the URL is ready for the Meta webhook
registration → see [`meta-webhook-registration.md`](meta-webhook-registration.md).

---

## Gotchas to carry in

- **24-hour service window** is stricter on IG than Messenger; set `UseHumanAgentTag=true` only if
  reps need to reply past 24h (extends to 7 days via the HUMAN_AGENT tag).
- **Token longevity** — prefer a **System User token** (non-expiring) for `PageAccessToken`; a dead
  token silently breaks outbound with no inbound symptom.
- **Dev-grade reliability** — in-memory conversation cache + polling thread; a restart drops active
  conversations. Fine for the Tester trial; plan step 8 hardens it.
