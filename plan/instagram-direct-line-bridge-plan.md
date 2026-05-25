# Instagram → D365 Contact Center — custom Direct Line bridge (implementation plan)

**Date:** 2026-05-23 · **Owner:** Chris · **Status:** ✅ **Approved — build now** (Chris 2026-05-23: "the Instagram bridge we can build now, and we'll build it… in a new conversation"). Execute in a dedicated build session.
**Why this exists:** Instagram has real inbound DM volume for AWD, but **D365 Contact Center has no native Instagram channel** and IG does **not** ride on the Facebook channel (separate Meta webhook + `instagram_manage_messages` permission the FB connector ignores). Confirmed via the canonical [Overview of channels](https://learn.microsoft.com/en-us/dynamics365/customer-service/use/channels) doc (social = Apple/Facebook/WhatsApp/LINE only), the live `crm11` UI, and roadmap absence. The supported way to land IG DMs in the **agent workspace** is a **custom channel via Direct Line** ("bring your own channel"). See [meta-channel-connectors §2](../docs/meta-channel-connectors.md) for the option comparison (SyncBox 365 = opaque quote-only pricing; Sprinklr = enterprise/overkill; generic iPaaS = record-sync only, not live chat).

## Decision on record

Build a **custom Direct Line bridge** rather than buy. Rationale: hosts on AWD's existing **$2,400/yr Azure credit** (≈ £0 incremental), £0 Meta per-message, and Microsoft ships a **complete reference connector to fork** — so it's ~1 day of focused build, not a from-scratch project. SyncBox has no transparent price (sales conversation required); Sprinklr is overkill at AWD volume.

## Architecture

```
Instagram DM  ──webhook──▶  Azure Function (the bridge)            ──Direct Line API 3.0──▶  D365 custom channel
(customer)                   ├─ Adapter Webhook API (IChannelAdapter)                          → workstream → HUMAN queue
                             ├─ Instagram channel adapter (IAdapterBuilder)                       (no bot, mirror WhatsApp/FB)
                             └─ Message relay processor (Direct Line client + watermark poll)
agent reply  ◀──IG Send API──┘  ◀────────────── outbound Activity ──────────────┘
```

- **Reference to fork:** Microsoft's official "bring your own channel" sample (MessageBird connector, .NET, Direct Line API 3.0): <https://github.com/microsoft/Dynamics365-Apps-Samples/tree/master/customer-service/omnichannel/bring-your-own-channel>. Doc: <https://learn.microsoft.com/en-us/dynamics365/customer-service/develop/bring-your-own-channel>.
- We only **replace the MessageBird adapter with an Instagram adapter**; the relay processor, Direct Line plumbing, and watermark polling (no-message-loss) are already written.

## Components (per the MS sample)

1. **Adapter Webhook API service** — `POST /postactivityasync` (`IChannelAdapter`). Receives Meta IG webhook events, returns 200/500. Also a `GET` verify endpoint for Meta's webhook handshake (`hub.challenge`).
2. **Instagram channel adapter** (`IAdapterBuilder`):
   - **Inbound:** verify the Meta `X-Hub-Signature-256` against the app secret → map the IG message payload to a Bot Framework `Activity` (`from.id` = IGSID, `text`, `channelData.channelType="Instagram"`, optional `conversationcontext`/`customercontext`). Activity payload **≤28 KB**.
   - **Outbound:** convert the agent's reply `Activity` → Instagram **Send API** call (`POST /<IG_BUSINESS_ID>/messages` with `recipient.id` = IGSID).
3. **Message relay processor** — Direct Line client keyed by user IGSID; starts a Direct Line conversation, polls activities by **watermark** until `endOfConversation`, relaying back activities whose `from.id == BotHandle`. Needs the **Direct Line secret** + **bot handle** from the **Azure Bot's Direct Line channel** (see "D365 wiring topology" below — it is *Azure-Bot-first*, not D365-issued).

## Prerequisites

**Meta side**
- Instagram **professional (Business/Creator)** account, **linked to the AWD Facebook Page** (Meta Business Suite → linked accounts) — AWD already posts/receives on IG, so this likely exists; verify.
- **Dedicated Meta app** (decided 2026-05-23 — NOT the FB Messenger app `27031932926443539`; keeps IG App Review independent of the in-flight FB review). Permissions: **`instagram_manage_messages`**, **`instagram_basic`**, `pages_show_list`, `pages_manage_metadata`.
- **Instagram messaging webhook** subscription (messages field on the IG/Page object) pointed at the bridge's webhook URL.
- A **long-lived Page/IG access token** (System User token preferred for non-expiry) for the Send API.
- IG account setting: **"Allow access to messages"** (Instagram → Settings → connected tools) enabled.

**Azure side** (Core Benefits Credits sub `95b2f141-…`, RG e.g. `awd-contactcenter-rg`)
- **Azure Function** (or small App Service) to host the bridge — on the credit (~£few/mo).
- **Key Vault** for secrets (Direct Line secret, IG token, app secret, webhook verify token).
- **Application Insights** for logging/observability.

**Azure Bot + D365 side** — *Azure-Bot-first* (corrected 2026-05-25; D365 does **not** mint the Direct Line secret):
- **Azure Bot** resource (name = **`BotHandle`**) + **Entra app registration** (app ID + client secret + tenant ID).
- **Direct Line** channel on that Azure Bot → yields the **Direct Line secret** (+ the bot handle).
- **D365 "Add Custom account"** (Copilot Service admin center → Channels → Messaging → Custom) consumes the Entra **app ID + client secret + tenant ID**; its **Callback information** exposes the D365 inbound endpoint.
- Point the **Azure Bot's messaging endpoint** at that D365 inbound endpoint.
- **Workstream** (Inbound/Messaging/Custom) → route to a **human queue** (no bot — mirror the AWD WhatsApp/Facebook pattern), unless we decide to front it with the Phase-2 deflection bot.
- The bridge consumes only `DirectLineSecret` + `BotHandle`; the Entra app ID/secret/tenant are D365↔Bot wiring, **not** bridge config.

## Build steps (ordered)

1. Fork the MS sample; get it building locally.
2. Implement the **Instagram adapter** (inbound verify+map, outbound Send API). Add the `GET` webhook-verify endpoint.
3. Config: Direct Line secret, IG token, app secret, verify token (via Key Vault / app settings).
4. Deploy to **Azure App Service** (the fork is an ASP.NET Core Web API, not a Function); note the public webhook URL. ✅ done — `https://awd-ig-bridge.azurewebsites.net`.
5. **Meta:** register the webhook URL + subscribe to IG messaging events; add the IG permissions (Standard Access works for **app Testers** in dev mode — no App Review needed to test).
6. **Azure Bot + D365:** create the Azure Bot (+ Entra app) and its **Direct Line** channel → set `DirectLineSecret` + `BotHandle`; add the **D365 custom account** (consumes the Entra app creds) + workstream → human queue; point the Azure Bot's messaging endpoint at the D365 callback endpoint.
7. **Dev-mode test:** Chris/Lachy (as app Testers) DM the IG account → confirm it lands in the agent workspace and the agent reply returns to Instagram.
8. **Hardening for prod** (the sample explicitly is *not* reliability/scale-hardened): replace the in-memory active-conversation dict + polling thread with a durable store (e.g. Azure Table/Service Bus) and retry/backoff; handle token refresh; handle attachments/story-reply payload shapes.
9. **App Review (Live mode):** submit `instagram_manage_messages` + `instagram_basic` for review **after the in-flight FB review clears** (Meta does whole-app review, one submission at a time). Screencast + reviewer instructions, same shape as the FB submission. ~10-day queue.

## Cost

- **Azure:** small Function + Key Vault + App Insights ≈ a few £/mo, **inside the $2,400/yr Core Benefits credit**.
- **Meta:** £0 for IG DMs in the 24-hour service window.
- **D365:** no extra SKU — custom channel is covered by the Contact Center licence.
- **vs alternatives:** SyncBox 365 = unknown subscription (quote-only); Sprinklr = £600–1,500/user/mo.

## Timeline

- **~1 day** of focused Claude-Code build → working **dev-mode** bridge (testable immediately with Testers).
- **+ App Review queue** (~10 days, after FB clears) before **real** IG customers can message in Live mode.

## Risks / gotchas

- **Whole-app App Review collision:** can't submit IG perms while the FB `pages_messaging` review is in flight → sequence IG review *after* FB approval.
- **IG 24-hour service window** (stricter than Messenger); the **HUMAN_AGENT** tag extends to 7 days — implement it for late agent replies.
- **Activity payload ≤28 KB** (Direct Line limit) — chunk/trim long messages + media refs.
- **Token longevity** — use a System User token or implement refresh; a dead token silently breaks outbound.
- **Reliability:** the MS sample is a reference, not production-grade (in-memory conversation cache + polling). Prod needs durable state + retries (step 8).
- **Media/stories:** IG sends story-replies/mentions/media with different payload shapes — map or gracefully degrade.

## Open decisions for Chris

1. ~~**Reuse the existing FB app** (`27031932926443539`) for IG perms, or a **dedicated app**?~~ **Resolved 2026-05-23 — dedicated app.** Avoids the whole-app-review collision entirely (Meta does whole-app review, one submission at a time); IG's review clock runs independently of the in-flight FB Messenger review.
2. **Human-first** (mirror WhatsApp/FB) or front with the **Phase-2 deflection bot** from day one?
3. ~~**When:** build the dev-mode bridge now (parallel to FB review) or after FB clears?~~ **Resolved 2026-05-23 — build now, in a dedicated session** (parallel to the in-flight FB review; the App-Review submission for Live mode still sequences after FB clears per step 9).
4. **Prod-hardening scope:** ship the dev-grade bridge for the trial, or invest in the durable/retry version before real traffic?
