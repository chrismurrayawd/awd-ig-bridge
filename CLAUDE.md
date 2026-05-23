# AWD Instagram → D365 Contact Center bridge — Project Guide

## What this project is

A standalone **.NET Azure Function** that bridges **Instagram Direct Messages** into the
**Dynamics 365 Contact Center** agent workspace, via Microsoft's officially-supported
**Direct Line API 3.0 / "bring your own channel"** path.

D365 Contact Center has **no native Instagram channel**, and Instagram DMs do **not** ride on
the Facebook channel (separate Meta webhook + `instagram_manage_messages` permission the FB
connector ignores). The supported way to land IG DMs in the agent desktop is a custom Direct Line
channel. We **fork Microsoft's reference connector** (MessageBird, .NET) and replace only the
adapter with an Instagram adapter — the relay processor, Direct Line plumbing, and watermark
polling are already written.

**This is the build session's job: execute [`plan/instagram-direct-line-bridge-plan.md`](plan/instagram-direct-line-bridge-plan.md).**
That plan is the spec. Read it first, in full.

> **Heads-up — this folder is the bootstrap, not the code yet.** As of scaffolding
> (2026-05-23) this folder contains the plan + context docs only. The .NET solution does not
> exist yet — **build step 1 is to fork the MS sample into this folder.** See "First moves" below.

## First moves (when you open this folder fresh)

1. **Read the plan** — [`plan/instagram-direct-line-bridge-plan.md`](plan/instagram-direct-line-bridge-plan.md). It has the architecture, components, ordered build steps, risks, and the decisions already taken.
2. **Read the context docs** (below) so you understand *why* this approach and the exact Meta/D365 field names.
3. **Resolve the two open decisions** still on the plan before writing code that depends on them (see "Open decisions" below).
4. **Build step 1:** clone Microsoft's sample into this folder, get it building locally with the .NET SDK, commit it as the baseline, then start replacing the MessageBird adapter.

## Reference to fork

- **Microsoft "bring your own channel" sample** (MessageBird connector, .NET, Direct Line API 3.0):
  <https://github.com/microsoft/Dynamics365-Apps-Samples/tree/master/customer-service/omnichannel/bring-your-own-channel>
- **Doc:** <https://learn.microsoft.com/en-us/dynamics365/customer-service/develop/bring-your-own-channel>
- The whole `microsoft/Dynamics365-Apps-Samples` repo is large; you only need that subtree.
  Sparse-checkout or copy the subtree in, don't carry the rest.

## The three components (per the MS sample)

1. **Adapter Webhook API** — `POST /postactivityasync` (`IChannelAdapter`); also a `GET` verify
   endpoint for Meta's webhook handshake (`hub.challenge`).
2. **Instagram channel adapter** (`IAdapterBuilder`):
   - **Inbound:** verify Meta `X-Hub-Signature-256` against the app secret → map the IG message
     payload to a Bot Framework `Activity` (`from.id` = IGSID, `text`,
     `channelData.channelType="Instagram"`). Activity payload **≤28 KB**.
   - **Outbound:** convert the agent reply `Activity` → Instagram **Send API** call
     (`POST /<IG_BUSINESS_ID>/messages`, `recipient.id` = IGSID).
3. **Message relay processor** — Direct Line client keyed by IGSID; starts a Direct Line
   conversation, polls activities by **watermark** until `endOfConversation`. Needs the
   **Direct Line secret** from the D365 custom channel.

**You only write component 2 (the Instagram adapter).** Components 1 and 3 come from the sample.

## Context docs (read before building)

- [`docs/meta-channel-connectors.md`](docs/meta-channel-connectors.md) — the connector option
  comparison. **§2 Instagram Direct** is the decisive section: why native/FB-link doesn't work,
  why the custom Direct Line bridge is the chosen path, why SyncBox 365 (opaque pricing) and
  Sprinklr (enterprise overkill) were rejected.
- [`docs/official-docs.md`](docs/official-docs.md) — canonical Microsoft Learn + Meta for Developers
  URLs with the **actionable facts already extracted** (D365 channel field names, App Review
  screencast requirements, dev-mode-vs-Live rules, token expiry). **Prefer these over guessing.**
- [`docs/d365-cc-context.md`](docs/d365-cc-context.md) — where this bridge sits in the wider AWD
  Dynamics 365 Contact Center programme (Phase 1 channels, the in-flight Facebook App Review this
  must sequence behind, the AWD-specific Meta/Azure IDs).

## Decisions already taken (do not re-litigate)

- **Build the bridge, don't buy.** Hosts on AWD's Azure credit (≈ £0 incremental), £0 Meta
  per-message, MS ships a forkable reference connector. (Rejected SyncBox 365 = quote-only; Sprinklr = overkill.)
- **Dedicated Meta app** for IG — **NOT** the FB Messenger app `27031932926443539`. Keeps IG's
  App Review independent of the in-flight FB `pages_messaging` review (Meta does whole-app review,
  one submission at a time). *(Resolved 2026-05-23, plan open-decision #1.)*
- **Build now**, in this dedicated session, parallel to the in-flight FB review. The Live-mode
  App-Review submission still sequences *after* FB clears (plan step 9). *(Resolved 2026-05-23, plan open-decision #3.)*

## Open decisions — resolve before the code that depends on them

These are still open on the plan. Get Chris's call before building the dependent piece:

1. **Human-first vs deflection bot (plan #2):** route the D365 workstream to a **human queue**
   (mirror the AWD WhatsApp/Facebook pattern) or front it with the Phase-3 deflection bot from
   day one? **Default = human-first.** This is a D365 *workstream-config* choice, not bridge code —
   it does not block the adapter build.
2. **Prod-hardening scope (plan #4):** ship the **dev-grade** bridge (sample's in-memory
   conversation dict + polling thread) for the Tester trial, then harden later (plan step 8) — or
   build the durable-store/retry/token-refresh version up front? **Default = dev-grade now**, harden
   after the flow is proven. This *does* change the code you write, so settle it first.

## What needs Chris (interactive — not autonomous)

The build splits into **code** (autonomous, do it here) and **portal/credential** steps
(Chris drives, with his logins):

| Plan step | Type | Who |
|---|---|---|
| 1–3 Fork + Instagram adapter + config wiring | **Code** | Claude Code (here) |
| 4 Deploy to Azure Function; get public webhook URL | Portal / Azure CLI | Chris (credentials) |
| 5 Meta: dedicated app, webhook subscription, IG permissions, Testers | Meta dev console | Chris |
| 6 D365: custom Direct Line channel + workstream → queue | D365 Copilot Service admin center | Chris |
| 7 Dev-mode test (Tester DMs the IG account) | Manual / Playwright | Chris + Lachy |
| 8 Prod hardening (durable state + retries + token refresh) | **Code** | Claude Code (here) |
| 9 App Review (Live mode) — **after FB review clears** | Meta submission | Chris |

## Azure target (Chris's tenant)

- **Subscription:** Core Benefits Credits sub `95b2f141-…` (the $2,400/yr Azure credit, expires
  2027-03-26). Small Function + Key Vault + App Insights ≈ a few £/mo — well inside the credit.
- **Resource group:** e.g. `awd-contactcenter-rg`. **Region:** UK South (data residency; fallback West Europe).
- **Secrets in Key Vault:** Direct Line secret (from the D365 custom channel), IG/Page access token
  (System User token preferred — non-expiry), Meta app secret, webhook verify token.
- **Application Insights** for logging/observability.

## Sign in as

For any Microsoft/Azure/D365 admin, Chris signs in as **`chris@chrismurray.eu`** (tenant Global
Admin). `alloywheelsdirect.com` is the mailbox/data domain, never the login.

## Risks / gotchas (from the plan — keep these in front of you)

- **IG 24-hour service window** (stricter than Messenger); the **HUMAN_AGENT** tag extends to 7
  days — implement it for late agent replies.
- **Activity payload ≤28 KB** (Direct Line limit) — chunk/trim long messages + media refs.
- **Token longevity** — a dead token silently breaks outbound. Use a System User token or implement refresh.
- **Reliability:** the MS sample is a reference, not production-grade (in-memory cache + polling) — plan step 8 hardens it.
- **Media/stories:** IG sends story-replies/mentions/media with different payload shapes — map or gracefully degrade.
- **Whole-app App Review collision:** can't submit IG perms while FB `pages_messaging` is in review → sequence IG review after FB approval (the dedicated-app decision keeps the *apps* separate, but be deliberate about review timing).

## Conventions for this project

- **.NET** (the sample is C# / .NET, Direct Line API 3.0). Follow the sample's structure; replace,
  don't rewrite, the adapter.
- **Secrets never in git.** App secret, Direct Line secret, IG token, verify token → Key Vault /
  app settings / `local.settings.json` (gitignored). `.gitignore` in this folder already excludes
  `local.settings.json`, `*.user`, `bin/`, `obj/`.
- **This is its own git repo** — separate from the AWD Laravel monorepo. Init git here when you
  fork the sample in.

## Provenance

Scaffolded 2026-05-23 from the AWD repo's D365 Contact Center docs
(`alloy-wheels-direct/.claude/reference/microsoft-integrations/d365-cc/`). The plan and the two
`docs/` files are copies of the source-of-truth there as of that date — if you change the approach
materially, reflect it back into the AWD repo's copy too so the two don't drift.
