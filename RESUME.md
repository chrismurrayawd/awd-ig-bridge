# RESUME — IG → D365 Contact Center bridge

## ✅ Current state (2026-05-30) — LIVE in production

The bridge is **live and serving real customer Instagram DMs in both directions** (inbound IG → D365
agent workspace; agent reply → IG), on the Linux App Service **`awd-ig-bridge`**. It runs under Meta
**Standard Access** as a first-party app on AWD's own Instagram account — **no App Review, no Advanced
Access, and no Tech Provider are needed** (do NOT submit App Review / do NOT become a Tech Provider).

**Host:** ASP.NET Core Web API on **Linux Azure App Service `awd-ig-bridge`** (RG `awd-contactcenter-rg`,
sub Core Benefits Credits `95b2f141-…`, UK South). Public webhook
`https://awd-ig-bridge.azurewebsites.net/api/InstagramAdapter/postactivityasync`.

**Live Azure app settings** (`awd-ig-bridge` — values live in App Service config / Key Vault, never in git):

| Setting | Value |
|---|---|
| `InstagramAdapterSettings__AppSecret` | **Instagram** app secret (app `1493427132185394`) — validates inbound `X-Hub-Signature-256` |
| `InstagramAdapterSettings__PageAccessToken` | **Instagram-user token** (`IGAA…`, ~60-day) — outbound Send API on `graph.instagram.com` |
| `InstagramAdapterSettings__IgBusinessId` | `17841440469975661` |
| `InstagramAdapterSettings__VerifyToken` | set (Meta GET webhook handshake) |
| `InstagramAdapterSettings__GraphApiVersion` | `v21.0` |
| `RelayProcessorSettings__DirectLineSecret` | set (from the Azure Bot's Direct Line channel) |
| `RelayProcessorSettings__BotHandle` | `awd-instagram-bot` |

**▶ In flight now — prod hardening (plan step 8).** Spec: [`docs/hardening-prompt.md`](docs/hardening-prompt.md);
phased plan: [`plan/hardening-step8-plan.md`](plan/hardening-step8-plan.md). Priority order:

- **P1 — Instagram-user token auto-refresh.** ⏰ **HARD DEADLINE ~late July 2026** — `PageAccessToken` was
  generated 2026-05-30 and expires ~60 days later; when it lapses, **outbound silently breaks**. Building now.
- **P2** observability (App Insights + structured logs) · **P3** durable conversation store (replace the
  in-memory cache + polling thread) · **P4** richer attachment/story handling · **P5** secret hygiene (rotate + Key Vault).

> The dated session log below is kept for diagnostics. It predates this header — some of its
> "pending / blocked / placeholder" notes are **superseded** by the live state above.

---

## Session log (history)

**Updated 2026-05-30 (session 3) — ✅ ROUND-TRIP COMPLETE + ✅ PRODUCTION-READY. Inbound AND outbound work end-to-end, AND the bridge serves REAL (non-Tester) customers under STANDARD access — so NO App Review, NO Advanced Access, NO Tech Provider is needed. Empirically confirmed 2026-05-30: a non-Tester IG account DM'd @alloywheelsdirect → open D365 conversation + agent reply delivered back. The dashboard's "become a Tech Provider" gate (irreversible-sounding) is for apps accessing OTHER businesses' accounts; AWD is first-party (own account) → Standard Access is sufficient per Meta's Instagram-platform docs. DO NOT submit App Review / DO NOT become a Tech Provider. Bridge can launch as-is.**

## ✅ COMPLETE 2026-05-30 (session 3) — full Instagram ↔ D365 round-trip is LIVE

Driven entirely by Claude in Playwright + az + dotnet (Chris: "finish the IG bridge, drive everything yourself").

**Proven working (2026-05-30 ~05:05 UTC):** real DM `chris_murray_ → @alloywheelsdirect` ("round trip test 6") → **open D365 Instagram conversation** (msdyn_channel 192350002, Default messaging queue); agent reply "how can we help?" → **arrived on chris_murray_'s Instagram ("Seen just now")**. Bidirectional.

### Two root causes fixed this session

**1. INBOUND (two parts):**
- **App was not published.** Meta's webhook-config panel literally says *"To receive webhooks, your app must be in published state."* In Dev mode, Meta delivered ZERO webhooks. **Published the app to Live** (Dashboard → Publish). To unlock the Publish button, set in App Settings → Basic: **Privacy policy URL** = `https://www.alloywheelsdirect.net/information/privacy_policy`, **User-data-deletion URL** = `…/privacy_policy#data-deletion` (§13, anchor `data-deletion`), **Category** = Messaging. (App icon NOT required.) After Live, Meta started delivering immediately.
- **Wrong app secret → every webhook 403.** After Live, Meta delivered but the bridge rejected EVERY POST with **HTTP 403** (Azure `Http403` metric — the only 403 path in `InstagramAdapterController.PostActivityAsync` is signature-validation failure). Cause: the bridge's `InstagramAdapterSettings__AppSecret` was the **Facebook app secret** (1531544178609736). **Instagram-API-with-Instagram-Login signs webhooks with the Instagram app secret** (app `1493427132185394`, on the "API setup with Instagram login" page). **Set `InstagramAdapterSettings__AppSecret` = Instagram app secret** → inbound validates (verified: IG-secret-signed synthetic POST → 200; bad-sig → 403).

**2. OUTBOUND (code + token):**
- `graph.facebook.com/{ig-id}/messages` (Page token) → **"(#3) Application does not have the capability"**. IG-Login apps must send via **graph.instagram.com** with an **Instagram-user token (IGAA…)**, not a Page token (the old `PageAccessToken` was a FB token `EAAV…`, rejected by graph.instagram.com with code 190).
- **Code change (deployed):** `InstagramClientWrapper.GraphApiBaseUrl` `https://graph.facebook.com` → **`https://graph.instagram.com`** (the bridge's existing `/{IgBusinessId}/messages` path + body incl. `messaging_type` are accepted as-is — verified via 2534014 "recipient not found", a recipient-only error).
- **Minted an Instagram-user token** (IG-login → step 2 "Generate access tokens" → @alloywheelsdirect → OAuth Allow; one-time-display IGAA… token, ~181 chars, ~60-day, scopes incl. `instagram_business_manage_messages`). **Set `InstagramAdapterSettings__PageAccessToken` = that IG token.**
- Built (`dotnet build`/`test` = 20 pass) → `dotnet publish` → **zip MUST use forward-slash paths** (PowerShell 5.1 `Compress-Archive` writes backslashes → Linux App Service rsync fails on `wwwroot\default.htm`; rebuilt the zip with Python `zipfile` + `.replace('\\','/')`) → `az webapp deploy --type zip` = **RuntimeSuccessful**.

### ⚠️ Follow-ups
- **CODE IS DEPLOYED BUT NOT COMMITTED.** `InstagramClientWrapper.cs` (graph.instagram.com) change is live on Azure but not in git — **commit it** or a future repo redeploy reverts outbound.
- **IG-user token expires in ~60 days (~late July 2026).** Outbound will silently break when it lapses → **token refresh (step 8) is now urgent**, not optional. (Inbound's AppSecret + VerifyToken don't expire.)
- **Sensitive values now in Azure config** `awd-ig-bridge`: `AppSecret`=IG app secret, `PageAccessToken`=IG-user token. The IG app secret + IG token (and the @alloywheelsdirect IG / FB-admin passwords, browser-autofilled) appeared in the session transcript — rotate if that's a concern (IG app secret has a "Reset" button; regenerate the token the same way).
- **120s "timer"** = `msdyn_liveworkstream.msdyn_screenpoptimeout_optionSet` = 120 (agent-accept toast), NOT an expiry. Conversation persists in the Default messaging queue; `msdyn_autocloseafterinactivity` = 2880 min (48h). Chris chose to leave as-is.
- **Step-8 hardening now timely** (we're in Live mode): (a) **observability** — diagnosis required Azure-metric archaeology because the app's `LogWarning`s never reached a readable sink (`az webapp log tail` stayed empty); add App Insights + structured logging. (b) **IG-token refresh** (~60d). (c) **durable conversation store** — the in-memory cache + polling thread means a restart drops active conversations (old conversations can't receive replies after a restart — that's why a *fresh* DM was needed to re-test outbound after deploy).

---

## ▶ (SUPERSEDED by session 3 above) RESUME HERE TOMORROW (2026-05-29 EOD state)

**What works now (verified):**
- ✅ **D365 inbound path FIXED + proven** — completed the workstream's "Set up Custom" (Direct Line) channel; a synthetic signed webhook → bridge → Direct Line → Azure Bot → **created a D365 conversation in the Default messaging queue** (msdyn_channel=192350002). Bridge + DL secret + Azure Bot endpoint all good.
- ✅ **Meta webhook + all 3 bridge keys set & verified** (AppSecret proven via HMAC; `__IgBusinessId`=17841440469975661; `__PageAccessToken` = **non-expiring long-lived** Page token, Azure updated → outbound durable).
- ✅ **Instagram Testers added + accepted:** `chris_murray_` (id 17841401769468210) and `alloywheelsdirect` (id 17841440469975661).
- ✅ **IG-Login subscription enabled:** under app → Instagram API → **"API setup with Instagram login" → step 2 "Generate access tokens"**, @alloywheelsdirect is connected, token generated, and **"Webhook Subscription" toggle = ON** ("Webhooks turned on").

**❌ STILL BLOCKED — real DM not delivered.** A real DM `chris_murray_ → @alloywheelsdirect` produces **ZERO inbound POST** to the bridge (Azure App Service Requests metric shows only the 5-min Always-On keepalive, before AND after enabling the IG-login subscription). So Meta still isn't firing the `messages` webhook. The synthetic test proved everything downstream of Meta works, so this is purely **Meta → bridge delivery**.

**TOMORROW — try in this order (each is quick; re-test the DM after each, and check the Requests metric / D365 for a non-voice conversation):**
1. **Re-verify the IG-Login app-level webhook is actually configured.** On **"API setup with Instagram login" → step 3 "Configure webhooks"**: confirm Callback URL = `https://awd-ig-bridge.azurewebsites.net/api/InstagramAdapter/postactivityasync`, Verify token set, and the **`messages` field is SUBSCRIBED** (green/verified). The per-account toggle is ON, but if the app-level `instagram` webhook field/callback for the IG-Login product isn't subscribed, nothing fires. (Earlier I set the callback on this tab, but re-confirm `messages` is ticked after the IG-login product changes.)
2. **Message-request status (LOWER priority — Chris confirmed chris_murray_ DOES follow @alloywheelsdirect, 2026-05-29).** Request-vs-inbox status actually depends on whether **@alloywheelsdirect follows chris_murray_ back** (recipient→sender), but Instagram usually still fires the `messages` webhook for pending requests, so this likely is NOT the blocker. Cheap rule-out only: have @alloywheelsdirect follow chris_murray_ back; and check @alloywheelsdirect's Message Requests folder to confirm the DM even arrived on IG's side. Do AFTER #1/#3.
3. **Publish the app to Live** (Dashboard → Publish). Meta docs: `messages` webhooks generally require **Live** mode; the Dev-mode "app-role" exception may not cover the IG-Login `messages` field. Live mode for the already-granted scopes (basic messaging) may not need full App Review for testing. Try it.
4. **Connected Tools toggle (mobile only):** the web settings page only shows "Message requests"; the legacy **"Allow access to messages" / Connected Tools** toggle (if it applies) is in the **IG mobile app** as @alloywheelsdirect → Settings → (Business/Professional) → … . Verify it's ON.
5. **Propagation:** the subscription was toggled ON at ~16:20 UTC and tested immediately — give it 5–15 min and retest in case of lag.

**Diagnostic levers:** Requests metric — `az monitor metrics list --resource <site> --metric Requests --interval PT1M` (a webhook = an extra request beyond the 5-min keepalive). D365 conversations — browser fetch on `org7a63d391.crm11.dynamics.com` → `GET /api/data/v9.2/msdyn_ocliveworkitems?$orderby=createdon desc&$top=5` (look for msdyn_channel≠192440000). Synthetic signed webhook recipe is in the session log if needed to re-prove downstream.

**Note on flow mixing:** inbound subscription is now **Instagram-Login** (graph.instagram.com, IG-user token, per-account toggle), while outbound still uses the **Facebook-Login Page token** (graph.facebook.com/{ig-id}/messages). Inbound webhook payload is identical either way so the bridge needs no inbound change; if outbound (agent reply) later fails, switch the bridge send to graph.instagram.com + the IG-user token (code change). FB-login page subscription is impossible here (`pages_manage_metadata` is an Invalid Scope for this Instagram-API app — confirmed).

---

**(Earlier session-2 note, superseded by the ▶ block above — kept for detail.)** **Meta webhook + all 3 bridge keys = DONE & verified.** The D365-inbound blocker described below was subsequently FIXED (see ▶). Older context follows.

## 2026-05-29 (session 2) — Meta webhook + keys DONE; D365 inbound does NOT create conversations (READ FIRST)

Driven entirely by Claude in Playwright + az CLI (Chris: "finish the IG bridge, drive everything yourself").

### ✅ DONE this session (Meta + Azure — the assigned "REMAINING" items)
- **Meta webhook configured + verified.** App "AWD Social Messaging" (1531544178609736) → Instagram API use case → **the editable `instagram`-object webhook lives under the "API setup with Instagram login" tab → step 3 "Configure webhooks"** (the "API setup with Facebook login" tab is **docs-only** — no editable webhook UI on this app; the classic `/webhooks/` page 301s to the dashboard). Set Callback URL = `https://awd-ig-bridge.azurewebsites.net/api/InstagramAdapter/postactivityasync`, Verify token = the `awdig-…` token → **Verify and save succeeded** (first attempt failed on a B1 **cold-start** timeout; **enabled Always On** + retried → ✅). **`messages` field SUBSCRIBED** (also comments/message_edit/message_reactions/messaging_postbacks/referral/seen on by default). The single app-level `instagram` webhook callback is shared regardless of FB-login vs IG-login outbound, so this is correct for the bridge's FB-login flow.
- **All 3 bridge keys set in Azure App Service `awd-ig-bridge`** (via clipboard→az, never echoed):
  - `InstagramAdapterSettings__AppSecret` = Meta app secret (32 chars). **Proven correct** — a self-signed synthetic webhook passed the bridge's `X-Hub-Signature-256` HMAC check.
  - `InstagramAdapterSettings__PageAccessToken` = Page token for **Alloy Wheels Direct** Page (175474759173346), minted via Graph API Explorer (User token w/ `instagram_basic,instagram_manage_messages,pages_show_list,pages_read_engagement,business_management` → Business Login granting Page+IG+Business+messaging → `/me/accounts`). **Interim & likely short-lived (~1 h)** since derived from a short-lived user token — **upgrade to a long-lived/System-User token** for anything beyond immediate testing.
  - `InstagramAdapterSettings__IgBusinessId` = **`17841440469975661`** (confirmed via `GET /175474759173346?fields=instagram_business_account`; not a secret).
- **Always On = true** on the App Service (was false → caused the first webhook-verify cold-start failure).
- Bridge GET-verify returns 200 + echoes challenge; POST with valid signature → 200.

### ❌ BLOCKER — inbound message never becomes a D365 conversation (downstream of Meta/bridge)
Two independent tests, **neither created a D365 conversation** (queried `msdyn_ocliveworkitems` — only **Voice** conversations exist, ever; channel `192440000`; zero messaging conversations):
1. **Self-signed synthetic IG webhook → bridge**: returned **200** (signature OK, bridge accepted) → no D365 conversation.
2. **Direct post to Direct Line** (using the bridge's own `RelayProcessorSettings__DirectLineSecret`): started a DL conversation + posted a user activity successfully → **no D365 conversation, no bot reply**.

Azure Bot `awd-instagram-bot` checks out: endpoint = the D365 callback `https://m-cdb50f85…omnichannelengagementhub.com/botchannel/incoming?orgId=cdb50f85…`, appId `6f368e7f…`, **SingleTenant**, tenant `c927edba…`, Direct Line channel enabled (v3). The "AWD Instagram" **workstream** (`ae0c4a33-8876-b07a-7b08-cca1ebafdc78`) is **Active** (statecode 0) but **`msdyn_lastvalidationstatus` is null** (never validated/published?).

**✅ ROOT CAUSE PINPOINTED (Contact Center admin center → Workstreams → AWD Instagram):** the workstream's **Custom channel is NOT set up** — it shows **"Custom ❌ Required → Set up your Custom channel" [Set up Custom]**. So despite session-1's RESUME claiming "Custom (Direct Line) channel created", the channel was **never actually attached/completed on this workstream**. Routing is fine (Route to queues → Default messaging queue), but there is **no live channel feeding the workstream** → inbound activities have nowhere to land → no conversation is ever created. This fully explains both negative tests.

**✅ FIX APPLIED 2026-05-29 (session 2)** — completed the **"Set up Custom" wizard** on the AWD Instagram workstream. Set **Custom Channel Type = Direct Line** → the wizard's **"Custom channel" step listed the existing `AWD Instagram` (Direct Line) registration as an "Available custom channel" and I selected it** (clean **reuse** — no duplicate, and it did **not** re-prompt for a client secret). Walked Language (English-US) / Behaviors (defaults) / User features (file attachments on) → **"Create channel" → "Custom channel created."** The workstream now shows the channel attached: **Channel ID = Direct Line, routed to Default messaging queue, 1 of 1 channels** — the "Custom ❌ Required" state is gone. **Omnichannel changes take up to ~15 min to propagate** before inbound starts creating conversations; verify with the synthetic signed webhook after that.

**✅ VERIFIED 2026-05-29 (session 2):** after the channel-attach, a **synthetic signed IG webhook → bridge (200) → created a D365 conversation** — `msdyn_ocliveworkitem` createdon `2026-05-29T14:30:20Z`, **`msdyn_channel` = 192350002 (Custom/Instagram)**, statuscode 2 (open), in the **Default messaging queue**. The inbound path (IG webhook → bridge → Direct Line → Azure Bot → D365 conversation) is now **fully working**. (Propagation was immediate, not the full 15 min.) Minor cleanup: that synthetic open conversation (fake sender `778899SYNTH…`) is sitting in the queue — close it if it bothers anyone.

### 2026-05-29 (session 2, later) — Tester round-trip attempted; NEW blocker: Meta not delivering inbound webhooks
- **Instagram Tester `chris_murray_` added + accepted** (App roles → Instagram Testers; status now active).
- **Real DM from `chris_murray_` → @alloywheelsdirect did NOT deliver any webhook** to the bridge — Azure App Service request metrics show only the 5-min Always-On keepalive (1 req/5 min, all 2xx), zero inbound POST around the DM. So Meta isn't delivering (this is upstream of the bridge/D365, which the synthetic test already proved good).
- **Page token had EXPIRED** (short-lived, ~90 min) → re-minted a **NON-EXPIRING long-lived Page token** (exchanged short user token → long-lived user token via app-secret `fb_exchange_token`, then `/me/accounts`; `debug_token` confirms `expires_at=0`). **Azure `InstagramAdapterSettings__PageAccessToken` updated** → outbound/reply path now durable. (Was a latent silent-outbound-failure, matching code-review finding "dead token breaks outbound silently.")
- **Inbound subscription is the blocker, and both Graph paths are blocked for this app:**
  - `POST/GET /{page-id}/subscribed_apps` → `(#200) Requires pages_manage_metadata` — and `pages_manage_metadata` is an **"Invalid Scope"** for this app (not configured / not available for an Instagram-API app; OAuth dialog rejects it).
  - `GET /{ig-id}/subscribed_apps` (graph.facebook.com) → `(#100) nonexisting field`; `POST` → `(#3) Application does not have the capability to make this API call.`
  - i.e. the app-level `instagram` webhook + `messages` field is subscribed and verified, but nothing **subscribes the actual IG account/Page to the app**, so Meta never fires the webhook.
- **✅ Research (cited) + confirmed in-app:** For the **Facebook-Login** flow the subscribe call is `POST graph.facebook.com/{PAGE-id}/subscribed_apps?subscribed_fields=messages` and it **requires `pages_manage_metadata`** — there is NO `instagram_manage_messages` substitute. **Confirmed `pages_manage_metadata` is NOT in this app's Use case → Permissions and features list** (only `pages_read_engagement`/`pages_show_list`/`business_management`/`instagram_*`/`public_profile`), and the OAuth dialog rejects it as "Invalid Scope". So the FB-login page-subscription is **impossible without adding a Messenger/Pages product** to unlock `pages_manage_metadata`. Meta's documented + recommended path is the **Instagram-Login** subscription: `POST https://graph.instagram.com/me/subscribed_apps?subscribed_fields=messages` with an **Instagram User access token** + `instagram_business_manage_messages` (no Page token / no `pages_manage_metadata`). Also required: the **@alloywheelsdirect IG account "Allow Access to Messages" (Connected Tools) = ON**, and **Live mode** for non-role senders (Tester works in Dev mode once subscribed). Dev-mode delivery to an accepted Instagram Tester is supported. Inbound webhook PAYLOAD is identical either flow (`object:instagram`/`messages`), so the bridge's **inbound code needs no change**; only the one-time subscription call differs. Outbound currently uses Page token via graph.facebook.com — may continue to work alongside an IG-login subscription, or may later need to move to graph.instagram.com + IG-user token (bridge change).

**Original hypotheses (now resolved by the above):** (a) add `pages_manage_metadata` to the app's Permissions & features, then `POST /{page-id}/subscribed_apps?subscribed_fields=messages`; or (b) the "Instagram API with **Facebook** Login" messaging path is deprecated/limited and the supported route is **"Instagram API with Instagram Login"** → `POST https://graph.instagram.com/{ig-id}/subscribed_apps?subscribed_fields=messages` with an **Instagram User token** (would be a bridge code change: graph.instagram.com + IG-user token, not Page token); or (c) the app must be in **Live/published** mode for `instagram` `messages` delivery even to Testers. Pending the research subagent's cited answer before changing config/code.

**STILL TODO — the REAL Tester round-trip (acceptance step 3):** inbound is proven with synthetic data; the real test needs a DM from an **accepted Instagram Tester** account (the GF account used earlier is not a Tester → Dev-mode won't deliver). Add Chris's/Lachy's IG under **App roles → Instagram Testers** and **accept the invite in that IG account's settings**, then DM `@alloywheelsdirect`, confirm the conversation lands, and have an agent reply (outbound: D365 → bot → Direct Line → bridge polls watermark → IG Send API with the Page token). Outbound hasn't been exercised yet (the synthetic sender IGSID is fake, so a reply to it would no-op).

Original fix notes (for reference):
- Step 1 **Custom Channel Type defaults to "Messaging API" — must be changed to "Direct Line"** (the bridge relays via the DirectLine secret + Azure Bot; it is NOT a D365 Messaging-API adapter).
- **Account details** step needs the bot's **App ID `6f368e7f-9078-4f3a-b292-d69263731afd`** + a **client secret** + **Tenant `c927edba-c37d-4a55-9432-3613529ca87b`**. The session-1 client secret was not stored → **mint a fresh secret** on that Entra app reg (`az ad app credential reset --id 6f368e7f… --append`) and paste it in.
- **Duplicate-registration risk:** session-1's RESUME notes a prior orphan `msdyn_ocbotchannelregistration` (same appId) that had to be deleted to allow create. Before/while completing the wizard, confirm there isn't a leftover/conflicting custom-channel "account" under **Channels → Accounts** that should be reused instead of creating a new one (otherwise the create can fail with `PreValidationCustomMessagingApplicationCreatePlugin` duplicate error).
- After completion: **Omnichannel config changes take up to 15 min to propagate** (admin-center banner). Then re-run the synthetic signed webhook (or a Tester DM) and re-query `msdyn_ocliveworkitems` for a non-voice (Instagram) conversation.

**Verified during diagnosis (so these are NOT the problem):** bridge signature/app-secret OK (synthetic signed POST → 200); DirectLine secret valid (started a DL conversation + posted an activity); Azure Bot endpoint/appId/tenant/SingleTenant correct; DL channel enabled. The ONLY missing link is the D365 Custom channel setup above.

### ⚠️ For the eventual REAL Tester DM round-trip (step 3, still pending)
- The app is in **Development mode**. Instagram message webhooks in dev mode are only delivered for senders that are **accepted Instagram Testers** on the app. Chris's GF account (used in this session's test attempt) is almost certainly **not** a Tester → that DM would not have been delivered by Meta regardless of the D365 blocker. Use an IG account added under **App roles → Instagram Testers** that has **accepted** the invite, or publish the app.
- Acceptance of step 3 = Tester DM `@alloywheelsdirect` → conversation in **Default messaging queue** → agent reply returns to IG. **Still blocked on the D365 inbound issue above.**

---

**Updated 2026-05-29 (session 1).** Code 1–3 done; deployed (step 4); **Azure Bot + Direct Line CREATED + bridge config wired (step 6 half-done)** — only the D365 custom-channel clicks + Meta webhook remain. **Read the 2026-05-29 section immediately below first;** older context follows.

## 2026-05-29 — Azure Bot + Direct Line CREATED; bridge config wired; D365 custom-channel is next (READ FIRST)

Driven by Claude (Chris: "go! create IG bot"). **Step 6 is now half-done — the Azure side exists; the D365 custom-channel clicks + Meta webhook remain.**

### Done this session (live, sub `95b2f141`, RG `awd-contactcenter-rg`)
- **Entra app reg `awd-instagram-bot`** (single-tenant) — **appId `6f368e7f-9078-4f3a-b292-d69263731afd`**, **tenant `c927edba-c37d-4a55-9432-3613529ca87b`**. SP created. **No client secret minted yet** — mint it at step D365-1 below (keeps it out of any transcript).
- **Azure Bot `awd-instagram-bot`** (Microsoft.BotService, SKU F0, app-type SingleTenant). Messaging endpoint is a **placeholder** (`https://awd-ig-bridge.azurewebsites.net/api/messages`) — **correct it to the D365 callback at step D365-3.**
- **Direct Line channel** added; secret captured → **set into the bridge App Service config**: `RelayProcessorSettings__DirectLineSecret` (✅ hidden) + `RelayProcessorSettings__BotHandle=awd-instagram-bot` (✅). Bridge now has **5 of its keys**; the remaining 3 are the Meta ones (`InstagramAdapterSettings__AppSecret`/`__PageAccessToken`/`__IgBusinessId`, step 5).

### ✅ 2026-05-29 ~12:45 — D365 custom channel DONE; workstream + Meta remain
- **DONE:** Deleted an orphan `msdyn_ocbotchannelregistration` ("AWD Instagram", appId `6f368e7f…`) that blocked create (`PreValidationCustomMessagingApplicationCreatePlugin` duplicate error). Re-ran the wizard → **"AWD Instagram" Custom (Direct Line) account + channel created** (Channels → Accounts; created 12:45). **Azure Bot messaging endpoint set** to the D365 callback `https://m-cdb50f85-ef55-f111-b7ad-6045bd0b35a3.uk.omnichannelengagementhub.com/botchannel/incoming?orgId=cdb50f85-ef55-f111-b7ad-6045bd0b35a3`. Plugin tracing reverted to off.
- **STILL REMAINING:**
  1. ✅ **DONE 2026-05-29 ~12:50** — "AWD Instagram" **Messaging** workstream created (wsid `ae0c4a33-8876-b07a-7b08-cca1ebafdc78`, streamsource 192350002 custom), **Push** distribution, routed to **Default messaging queue** (`85e55877…`, the human queue WhatsApp/FB use, cs@ is a member). Channel = Custom (AWD Instagram). **The entire D365 + Azure side is now complete.**
  2. **Meta (step 5):** subscribe the IG **`messages`** webhook to `https://awd-ig-bridge.azurewebsites.net/api/InstagramAdapter/postactivityasync`; mint an interim Tester **PageAccessToken**; fetch **`IgBusinessId`** (`GET /175474759173346?fields=instagram_business_account`) → set the 3 `InstagramAdapterSettings__AppSecret/__PageAccessToken/__IgBusinessId` bridge keys.
  3. **Dev test:** Tester DMs `@alloywheelsdirect` → workspace → reply returns.

#### Original ~6-click flow (now mostly done — kept for reference)
### Remaining — D365 custom Direct Line channel (the ~6-click flow; Azure-Bot-first)
- **D365-1 — mint the bot client secret (portal; never paste into chat):** Azure portal → Entra ID → App registrations → **awd-instagram-bot** (`6f368e7f-…`) → Certificates & secrets → **New client secret** → copy the **Value**.
- **D365-2 — create the custom channel:** Copilot Service admin → **Channels → Messaging → Add channel → Channel = Custom → Custom Channel Type = Direct Line → Add a Custom account** → enter **app ID `6f368e7f-9078-4f3a-b292-d69263731afd`** + **Client secret** (D365-1) + **Tenant ID `c927edba-c37d-4a55-9432-3613529ca87b`** → **Validate**. Finish Custom channel + **Callback information** → **copy the D365 callback/inbound endpoint URL**.
- **D365-3 — point the bot at the callback:** set Azure Bot `awd-instagram-bot` **messaging endpoint = that callback URL** (`az bot update -g awd-contactcenter-rg -n awd-instagram-bot --endpoint "<url>"`).
- **D365-4 —** create the **"AWD Instagram"** Direct Line channel + route its **workstream → human queue** (mirror WhatsApp/FB; no bot).

### Then — Meta (step 5) + dev test (step 7)
- Subscribe the IG **`messages`** webhook to **`https://awd-ig-bridge.azurewebsites.net/api/InstagramAdapter/postactivityasync`** (VerifyToken already set), add an interim Tester **PageAccessToken** (Graph API Explorer; app "AWD Social Messaging" `1531544178609736`), fetch **`IgBusinessId`** (`GET /175474759173346?fields=instagram_business_account`). Set those 3 into the bridge App Service config.
- **Dev test:** Chris/Lachy (app Testers) DM `@alloywheelsdirect` → lands in agent workspace → reply returns to IG.

### Aside — voicemail diagnostics (unrelated to IG; for the record)
ACS resource diagnostics on `awd-cc-voice-acs` stay empty for the Teams-Phone-routed voice number because TPE/trial calls run Call Automation+recording on the **Microsoft-managed CCaaS ACS**, not the customer resource (per a 2026-05-29 research subagent). ACS diagnostics is the wrong instrument there; use D365 conversation diagnostics / Teams CQD / an ACS-direct-number A/B. Voicemail topology diagnosis stands.

---


## Live deployment (step 4)

| | |
|---|---|
| Public URL | **https://awd-ig-bridge.azurewebsites.net** |
| Webhook endpoint | **https://awd-ig-bridge.azurewebsites.net/api/InstagramAdapter/postactivityasync** |
| Host | Linux App Service `awd-ig-bridge`, plan `awd-ig-bridge-plan` (B1), **UK South** |
| Resource group | `awd-contactcenter-rg` (metadata region SE Asia — cosmetic; resources are uksouth/global) |
| Subscription | Core Benefits Credits `95b2f141-b4c6-4e9c-8d69-254c5be3baf9` |
| Verify token | set as app-setting `InstagramAdapterSettings__VerifyToken` (value shared with Chris directly — **not committed**) |
| Verified live | GET valid→challenge/200, wrong-token→403, unsigned POST→403, empty→400 |
| App-settings (all live ✅) | `RelayProcessorSettings__DirectLineSecret` (🔒) + `BotHandle` (from the **Azure Bot** / its Direct Line channel, step 6), Meta `AppSecret`/`PageAccessToken`/`IgBusinessId` (step 5) — all set & live as of 2026-05-30 (see **Current state** header; values in App Service config, never committed). Exact key list + secret/non-secret split in `docs/azure-deploy-notes.md` §3 |

## Where the code is

| | |
|---|---|
| Branch | `main` |
| HEAD commit | see `git log` (latest doc/deploy commits on top of the step 1–3 work) |
| Step 2–3 commit | `e096540` — "Step 2-3: replace MessageBird adapter with Instagram adapter + config wiring" |
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
| 4 | ✅ Deployed to App Service (UK South); GET-verify live. Remaining: wire real secrets to Key Vault once they exist | done (Claude) / Chris |
| 5 | Meta: dedicated app, webhook + IG perms + Testers | Chris |
| 6 | **Azure Bot** (+ Entra app) → its **Direct Line channel** yields `DirectLineSecret` + `BotHandle`; **D365 custom account** consumes the Entra app creds; point the bot's messaging endpoint at the D365 callback; workstream → human queue | Chris |
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
- ✅ **Dev-grade now, harden later** (step 8). **(UPDATE 2026-05-30 — now LIVE; hardening is IN FLIGHT — P1 IG-token refresh first; see Current state header.)** Confirmed 2026-05-23; **re-confirmed 2026-05-25 (defer)** —
  harden after the dev-mode round-trip is proven, before Live-mode traffic. Known dev-grade gaps:
  restart drops in-memory conversations, no transient retry, dead token breaks outbound silently.
- ✅ **Human-first routing** by default for the D365 workstream (mirror WhatsApp/FB); not bridge code.
- ✅ **Host = Linux App Service** (resolved 2026-05-24). The fork is an ASP.NET Core Web API, which App
  Service hosts with zero code change; Azure Functions would need a port and isn't used. See
  `docs/azure-deploy-notes.md`.
- ✅ **Azure infra standardised** (2026-05-24): subscription `95b2f141-b4c6-4e9c-8d69-254c5be3baf9`
  (Core Benefits Credits); **reuse RG `awd-contactcenter-rg`** (no separate IG RG); deploy via explicit
  `dotnet publish` → `Compress-Archive` → `az webapp deploy --type zip`.
- ⤫ **(SUPERSEDED 2026-05-30 — the bridge is now fully LIVE; full DM round-trip works.)** Deploy-now is GET-verify-only on purpose (2026-05-24): the **Azure Bot + Direct Line channel**
  that issues the Direct Line secret doesn't exist yet (the D365/Contact Center side is mid-reprovision
  after trial→production), so `RelayProcessorSettings__DirectLineSecret` + the Meta token stay
  placeholders; `VerifyToken` is set so Meta's webhook handshake verifies. Full DM round-trip lights up
  once the Direct Line secret exists.
- ✅ **D365 wiring is Azure-Bot-first** (corrected 2026-05-25): D365 does **not** mint the Direct Line
  secret — you create an Azure Bot (name = `BotHandle`) + Entra app + Direct Line channel (→ secret), and
  D365's custom account consumes the Entra app creds. **Bridge code needs no change — config-only.** The
  Entra app ID/secret/tenant are D365↔Bot wiring, not bridge settings. See `CLAUDE.md` → "D365 wiring
  topology" and `plan` → "Azure Bot + D365 side".

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
