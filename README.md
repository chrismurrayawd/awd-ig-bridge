# awd-ig-bridge

**Instagram Direct → Dynamics 365 Contact Center bridge.** A .NET Azure Function that relays
Instagram DMs into the D365 agent workspace via Direct Line API 3.0 ("bring your own channel"),
forked from Microsoft's reference connector.

> **Status: dev-grade bridge built (plan steps 1–3 done).** The MS sample is forked in, migrated to
> **net8.0**, and the MessageBird adapter has been replaced with an **Instagram adapter** (inbound
> signature-verify + payload→Activity map, outbound Activity→IG Send API, plus the GET webhook-verify
> endpoint). Builds clean; all tests pass. Remaining steps (Azure deploy, Meta app, D365 channel,
> dev-mode test, App Review — plan steps 4–7, 9) are **portal/credential** work Chris drives. See the
> per-step ownership table in [`CLAUDE.md`](CLAUDE.md).

## Start here

1. Open this folder in **VS Code** and start a **Claude Code** session in it.
2. Claude reads [`CLAUDE.md`](CLAUDE.md) automatically — it's the project guide and tells the session
   exactly how to begin.
3. Tell Claude: **"execute the plan"** — i.e. work through
   [`plan/instagram-direct-line-bridge-plan.md`](plan/instagram-direct-line-bridge-plan.md).

## What's in here

```
CLAUDE.md                                    ← project guide (Claude Code reads on open)
README.md                                    ← this file
.gitignore                                   ← .NET + secrets
plan/
  instagram-direct-line-bridge-plan.md       ← THE plan to execute (architecture, steps, risks)
docs/
  meta-channel-connectors.md                 ← why this approach (IG option comparison, §2)
  official-docs.md                           ← canonical MS Learn + Meta URLs + extracted facts
  d365-cc-context.md                         ← where this fits in AWD's wider D365 programme
```

The .NET solution lives at the repo root (`Microsoft.OmniChannel.Connector.Sample.sln`). The
Instagram adapter is the project we wrote; everything else is forked from Microsoft's sample:

```
Microsoft.OmniChannel.Connector.Sample.sln            ← open this in Visual Studio
Directory.Build.props · NuGet.config                  ← repo-wide net8 build settings + nuget.org feed
Libraries/
  Microsoft.OmniChannel.Adapter.Builder/              ← IAdapterBuilder, ChannelType, ActivityExtension (sample)
  Adapters/
    Microsoft.OmniChannel.Adapters.Instagram/         ← OUR adapter (verify + map + Send API)
    Microsoft.OmniChannel.Adapters.Line/              ← sample's LINE adapter, left intact
Microsoft.OmniChannel.MessageRelayProcessor/          ← Direct Line client + watermark polling (sample)
Microsoft.OmniChannel.Adaptors.Service/               ← ASP.NET Core host; Controllers/InstagramAdapterController.cs
Tests/                                                ← xunit; Instagram helper + controller tests
```

## Build & run locally

```powershell
dotnet build Microsoft.OmniChannel.Connector.Sample.sln     # or open the .sln in Visual Studio
dotnet test  Microsoft.OmniChannel.Connector.Sample.sln
dotnet run --project Microsoft.OmniChannel.Adaptors.Service  # serves the webhook
```

The webhook (both Meta's GET verification handshake and POST events) is at:

```
/api/InstagramAdapter/postactivityasync
```

Expose it to Meta with ngrok during dev-mode testing (`ngrok http <port>`), then register the
HTTPS forwarding URL + verify token in the Meta app's webhook settings (plan step 5, Chris).

## Configuration & secrets

Settings bind from the `InstagramAdapterSettings` / `RelayProcessorSettings` sections of
`appsettings.json` (which holds **placeholders only** — never commit real secrets):

| Setting | What it is |
|---|---|
| `InstagramAdapterSettings:AppSecret` | Meta app secret — validates the inbound `X-Hub-Signature-256` |
| `InstagramAdapterSettings:VerifyToken` | Token echoed in Meta's GET webhook handshake |
| `InstagramAdapterSettings:PageAccessToken` | Long-lived Page/IG token for the Send API |
| `InstagramAdapterSettings:IgBusinessId` | IG-business / Page id; path of `POST /{id}/messages` |
| `InstagramAdapterSettings:GraphApiVersion` | e.g. `v21.0` (optional) |
| `InstagramAdapterSettings:UseHumanAgentTag` | `true` to tag replies HUMAN_AGENT (7-day window) |
| `RelayProcessorSettings:DirectLineSecret` | Direct Line secret from the D365 custom channel |
| `RelayProcessorSettings:BotHandle` | Direct Line bot handle |

For **local** real values, override without touching `appsettings.json` via environment variables
(double-underscore = section nesting), e.g. `setx InstagramAdapterSettings__AppSecret "…"`, or use
`dotnet user-secrets`. In **production** these come from **Key Vault / Function app settings**
(plan step 4, Chris).

## The split: code vs portals

- **Autonomous (Claude Code does it here):** fork the sample, write the Instagram adapter + webhook
  verify endpoint + config wiring (plan steps 1–3), prod hardening (step 8).
- **Interactive (Chris drives, with his Microsoft/Meta logins):** Azure Function deploy, Meta
  dedicated-app + webhook + permissions, D365 custom channel + workstream, App Review (steps 4–7, 9).

`CLAUDE.md` has the full per-step ownership table.

## Two decisions to settle before building the dependent code

1. **Human-first vs deflection bot** (D365 workstream config — default human-first, doesn't block code).
2. **Dev-grade vs hardened up front** (changes the code you write — settle first; default dev-grade).

See `CLAUDE.md` → "Open decisions".

## Provenance

Scaffolded 2026-05-23 from `alloy-wheels-direct/.claude/reference/microsoft-integrations/d365-cc/`.
Keep this repo and the AWD copy from drifting — material changes here should be reflected back there.
