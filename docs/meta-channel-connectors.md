# Meta Channels → Dynamics 365 Contact Center: Connector Map

**Last reviewed:** 2026-05-20
**Owner:** Chris / Tarquin
**Status:** Day 2 of D365 Contact Center build — Meta channels (FB Messenger, Instagram DM, WhatsApp)
**Scope:** Map the available connector paths from each Meta surface into D365 Contact Center, with cost, setup time, approval gates, and a recommendation per channel based on AWD's profile (low-to-medium e-commerce inbound, primarily customer-initiated).

---

> **CORRECTION 2026-05-20 (verified live during Phase-1 execution):** this doc's claims that "Meta Business Verification is already complete / done / Verified" (see §1.4, §3.4 step 1, §3.7 table) are **WRONG**. As of 2026-05-20 the Alloy Wheels Direct business was **unverified**; verification was **submitted that day and is In review (~2 working days, verdict ~22 May)**. The Facebook public-go-live chain is therefore: business verification → Tech Provider → App Review for `pages_messaging`. Dev-mode testing (app testers) works without it. See [phase1-execution-log-2026-05-20.md](../logs/phase1-execution-log-2026-05-20.md).

## TL;DR — the three recommendations

| Channel | Recommended path | Why |
|---|---|---|
| Facebook Messenger | **Native D365 Facebook channel** | First-party connector. No middleware fee. Works against the existing AWD FB Page. |
| Instagram Direct | **Native D365 Facebook channel, linked Instagram inbox** (via FB Page → Meta Business Suite "Connected Accounts") | There is no first-party D365 "Instagram channel" SKU. The pragmatic path is to route Instagram DMs through the Facebook Page inbox that D365 already subscribes to. If volume justifies it later, evaluate Sprinklr or a custom Direct Line channel. |
| WhatsApp Business | **Native D365 WhatsApp channel via Azure Communication Services (ACS) Advanced Messaging** | Microsoft's first-party path, GA since May 2025. ACS becomes the billing entity for Meta charges (single Azure invoice). Avoids paying a Twilio/MessageBird markup. |

---

## 1. Facebook Messenger

### 1.1 Connector options

| Option | Description | Vendor cost on top of D365 |
|---|---|---|
| **(a) Native D365 Facebook channel** | First-party connector configured via Copilot Service admin center → Channels → Messaging accounts → Facebook. Webhook callback to Meta Page subscription. | £0 connector fee — included in Contact Center Digital licence |
| (b) ACS Advanced Messaging bridge | **Not applicable** — ACS Advanced Messaging covers WhatsApp (and SMS), not Messenger | n/a |
| (c) Third-party (Twilio, MessageBird, Sprinklr) | Run Messenger through a CPaaS, push to D365 via custom Direct Line channel | Typically £200–£800/month platform fee + £0.005–£0.02 per message inbound/outbound. Adds latency and a billing surface. |

### 1.2 Cost (Microsoft side)

- **Licence:** Dynamics 365 Contact Center Digital — $95/user/month list. 40% promotional discount Oct 1 2025 → June 30 2026, effectively **$57/user/month** during the promo window.
- **Per-message:** £0 for the Facebook channel itself. Meta does not charge for Messenger messages in a 24-hour service window. Message-tag responses outside 24h are allowed for limited use cases without per-message cost.

### 1.3 Setup time

**~60–90 minutes** end-to-end if Meta Business Verification is already done (which AWD has).

### 1.4 Approval gates

- Meta Business Verification: **already complete** (Chris).
- Facebook App must be created in `developers.facebook.com` and switched from Development → Live mode. App review is required if the app will be used by Facebook users without an app role; the `pages_messaging` permission requires app review for production.
- App review for `pages_messaging`: typically 3–7 business days.

### 1.5 Permissions required in Meta Business Manager

- **Facebook Page role:** Admin on the AWD Facebook Page (to install the app and accept the page token).
- **Meta Business Manager:** Business Admin to add the Facebook App as a Business Asset.
- **App permissions to request:** `pages_messaging` (mandatory), `pages_messaging_subscriptions`, `pages_show_list`, `pages_manage_metadata`. Optionally `business_asset_user_profile_access` if you want customer names pulled into the contact record.
- **Access token:** Page Access Token (60-day expiry — refresh on schedule or use a long-lived token).

### 1.6 Channel-specific gotchas

- **24-hour rule:** representatives can reply freely within 24 hours of the customer's last message. Outside that window, the "human agent" message tag must be enabled in both Facebook app and D365 workstream (extends to 7 days).
- Page access tokens **expire every 60 days** unless rotated to a long-lived/system-user token. Put this on a renewal calendar.

---

## 2. Instagram Direct

> **⛔ CORRECTION 2026-05-23 — option (a′) below is WRONG. Instagram does NOT ride on the D365 Facebook channel.** Verified three ways: (1) the canonical Microsoft [Overview of channels](https://learn.microsoft.com/en-us/dynamics365/customer-service/use/channels) doc (updated 2026-05-08) lists supported social channels as **Apple Messages, Facebook, WhatsApp (Twilio), LINE only** — no Instagram; (2) firsthand — no "Instagram" option in any D365 channel dropdown in the live `crm11` build; (3) **not on the 2025-W2 / 2026 release roadmap** either. IG messaging uses a separate Instagram-object webhook + `instagram_manage_messages`, which the D365 Facebook connector (subscribed only to the Page `messages` field for Messenger) does **not** consume. So linking IG to the FB Page surfaces IG DMs in the **Meta Business Suite** unified inbox, NOT in the D365 agent workspace. Treat option (a′) as struck-through.
>
> **BUT it IS achievable via a bridge** (deep-research 2026-05-23). Meta's Messenger Platform exposes IG DMs over the Graph API when the IG professional account is linked to the FB Page (webhooks + `instagram_manage_messages`/`instagram_basic`). Real routes to get IG into the **D365 agent workspace**:
> 1. **Off-the-shelf AppSource add-on — `SyncBox 365` (Systems Limited):** markets itself as *"an add-on to Microsoft Dynamics 365 Contact Center… bridge the gap between Dynamics 365 Omnichannel and leading social media platforms"*, "Azure-powered", "real-time". **Most pragmatic** — but confirm (a) Instagram is in its channel list and (b) pricing, by opening the AppSource listing in a browser (it 403s scrapers): `marketplace.microsoft.com/en-gb/product/dynamics-365/systemslimited1589804773133.syncbox365`.
> 2. **Custom Direct Line / Bot Framework bridge (build-your-own) — RECOMMENDED for AWD.** IG Graph API webhook → relay → D365 **custom channel** via [Direct Line API 3.0 / "bring your own channel"](https://learn.microsoft.com/en-us/dynamics365/customer-service/develop/bring-your-own-channel) (officially supported). **Microsoft ships a complete reference connector** (MessageBird, .NET) to fork: <https://github.com/microsoft/Dynamics365-Apps-Samples/tree/master/customer-service/omnichannel/bring-your-own-channel> — the relay processor / Direct Line plumbing / watermark polling is already written; you only write the **Instagram adapter** (verify Meta webhook → map payload to a Bot `Activity`; agent reply `Activity` → Instagram Send API). Components per the doc: **Adapter Webhook API** (`IChannelAdapter.PostActivityAsync`), **Channel adapter** (`IAdapterBuilder` — inbound signature-validate + payload→Activity, outbound Activity→IG Send API), **Message relay processor** (Direct Line client + active-conversation dict + watermark poll). Needs a **Direct Line secret** from the D365 custom channel. **Cost:** host on AWD's Azure credit (small App Service/Function ≈ a few £/mo, within the $2,400/yr) + £0 Meta per-msg. **Effort:** ~1 day of focused build for a **dev-mode** bridge (testable with app Testers immediately); calendar long-pole = Meta **App Review for `instagram_manage_messages`/`instagram_basic`** to message real customers in Live mode (same ~10-day queue as FB; can't run concurrently with the in-flight FB review → submit IG perms after FB approves). Activity payload ≤28 KB; `channelData.channelType` = e.g. "Instagram"; `conversationcontext`/`customercontext` carry routing + contact-match data.
> 3. **Sprinklr** — native two-way D365 IG connector, enterprise pricing (£600–1,500/user/mo), overkill at AWD volume.
> 4. **❌ NOT viable for live agent chat:** generic iPaaS (Albato, Workato, Onlizer, ApiX-Drive, Integrately, Zapier-likes) only **sync IG messages as CRM records/notifications** — they do NOT route live two-way conversations into the Omnichannel agent desktop. Wrong tool for contact-centre IG.
>
> **Stance (updated 2026-05-23):** IG DMs remain answerable in the **Meta Business Suite inbox** (free) **until the bridge ships**. Chris has **approved building the custom Direct Line bridge now** (rejected SyncBox 365 = quote-only opaque pricing; Sprinklr = overkill) — see [instagram-direct-line-bridge-plan](../plan/instagram-direct-line-bridge-plan.md), executed in a dedicated build session.

### 2.1 Connector options

| Option | Description | Status / cost |
|---|---|---|
| **(a) Native D365 Instagram channel** | Standalone Instagram-only channel in admin center | **Does not exist.** Instagram is not listed as a first-party channel in D365 Contact Center as of May 2026. |
| (a′) Native D365 Facebook channel, with Instagram inbox linked at the Meta side | Connect the Instagram Business account to the Facebook Page in Meta Business Suite. Messages then flow into the same Page inbox that D365 subscribes to via `pages_messaging`. Requires `instagram_manage_messages` permission on the FB app. | £0 connector fee. **This is the pragmatic Microsoft-blessed path** for now. |
| (b) ACS Advanced Messaging bridge | Not available for Instagram. ACS Advanced Messaging covers WhatsApp + SMS only. | n/a |
| (c) Third-party (Sprinklr, Sprout Social, custom Direct Line) | Sprinklr Service has a native D365 connector for Instagram (two-way). Sprout Social has a D365 integration but is **one-way only** (pushes social messages into D365; no reply from D365). | Sprinklr: enterprise pricing, typically £600–£1,500/user/month. Sprout: ~£250+/user/month. Both overkill for AWD's current volume. |

### 2.2 Cost (Microsoft side)

- **Licence:** Same Contact Center Digital licence covers the Instagram-via-FB path. No additional Microsoft SKU.
- **Per-message:** £0. Meta does not charge for Instagram DMs in the service window.

### 2.3 Setup time

**~30 minutes** *after* the Facebook channel is live. It's almost entirely a Meta-side action (link Instagram to the FB Page + add `instagram_manage_messages` permission to the FB app, then re-trigger app review if going to Live mode).

### 2.4 Approval gates

- Instagram account must be a **Business or Creator account** (not Personal). Chris already has business pages.
- Instagram account must be **connected to the Facebook Page** in Meta Business Suite.
- Facebook App requires `instagram_manage_messages` permission — separate app review step from `pages_messaging`. Typically reviewed in the same submission (~3–7 business days).

### 2.5 Permissions required in Meta Business Manager

- Admin on the Instagram Business account in Meta Business Manager.
- Admin on the linked FB Page.
- `instagram_manage_messages`, `instagram_basic`, `pages_manage_metadata` on the FB app.

### 2.6 Channel-specific gotchas

- Instagram DM has a stricter **24-hour service window** than Messenger. The "human agent" tag is available but Instagram does not honour message tags as flexibly as Facebook does.
- Story replies and mentions arrive via the same webhook but with different payload shapes — confirm D365's parsing covers them, or expect some conversations to look thin.
- Group DMs are not supported through the API.

---

## 3. WhatsApp Business

### 3.1 Connector options

| Option | Description | Vendor cost on top of D365 |
|---|---|---|
| **(a) Native D365 WhatsApp channel via ACS Advanced Messaging** | First-party. GA May 2025. ACS is the BSP and Azure billing entity. Configured in admin center → Channels → Messaging accounts → WhatsApp → Provider: Azure Communication Services. | ACS messaging fee + Meta per-message fee, billed on one Azure invoice. See §3.4. |
| (b) Native D365 WhatsApp channel via **Twilio** as provider | Same admin center experience, but with Twilio selected as provider. Older path; pre-dates the ACS GA. | Twilio platform fee (none baseline) + Twilio markup of ~$0.005/message **on top of** the Meta rate. |
| (c) Third-party (MessageBird/Bird, Infobip, 360dialog) → custom Direct Line | Run WhatsApp through a non-Microsoft BSP; push into D365 via custom messaging channel | Markup of $0.005–$0.02/message + monthly platform fee (~£50–£250). No advantage for a first-time setup. |

### 3.2 Cost (Microsoft side) — ACS path

| Line item | Rate | Notes |
|---|---|---|
| D365 Contact Center Digital licence | $95/user/month list ($57 promo until 30 Jun 2026) | Same licence covers all three Meta channels |
| ACS Advanced Messaging usage fee — inbound | $0.005/message | Charged regardless of category |
| ACS Advanced Messaging usage fee — outbound | $0.005/message | Charged regardless of category |
| Meta WhatsApp charge | Per-message, varies by category & country (see §3.5) | Pass-through; appears as a line item on the Azure bill |
| ACS resource fixed monthly fee | $0 | No standing channel-registration fee documented as of 2026-05 |

**No standing monthly Azure fee** for the ACS resource itself beyond per-message usage.

### 3.3 Setup time

- Happy path with verified business + display name pre-approved: **2–3 hours** of work, spread over **3–5 calendar days** waiting for Meta display-name review.
- Worst case (rejected display name, OTP issues with overseas SIM): **5–10 calendar days**.

### 3.4 Migration / step-by-step (Meta Business Manager → D365)

This is the operational core. Tony's SIM is the linchpin step.

1. **Confirm Meta Business Verification.** Already done. Visible in Meta Business Manager → Security Center → Business Verification = Verified.
2. **Create a WhatsApp Business Account (WABA)** in Meta Business Manager → Accounts → WhatsApp accounts → Add. This is *not* the consumer WhatsApp Business app — it's a separate cloud-API construct.
3. **Add the UK phone number to the WABA.**
   - The number **must not** be active in the consumer WhatsApp app or WhatsApp Business app. If it is, delete the WhatsApp account on the device first (Settings → Account → Delete my account). This frees the number to register on the Cloud API.
   - Disable two-step verification on the existing WhatsApp instance before deletion.
4. **The OTP step (this is what Tony does).**
   - When you add the number, Meta sends a **6-digit OTP via SMS or voice call** to the SIM.
   - Tony, who physically holds the SIM, receives the OTP and reads it to Chris (or enters it directly into Meta Business Manager if he has a Business Manager role).
   - If SMS fails (common with international roaming), retry with the **voice call** option — Meta will phone the number and read the OTP aloud.
   - **Workaround if Tony's SIM has no signal in his current country:** the SIM has to either (a) be roaming-active and receive the OTP, or (b) get physically returned to a location with signal, or (c) you change to a different UK number you control. There is no Meta-side override; the OTP is the proof-of-possession.
5. **Submit a display name for review.** This is the customer-facing name shown above messages (e.g. "Alloy Wheels Direct"). Meta reviews against [display-name guidelines](https://developers.facebook.com/docs/whatsapp/overview/business-name-policy). Typical turnaround: **1–3 business days**. Common rejections: "Sales", "Support", or any business-descriptor-only name. Use the brand name as registered.
6. **Provision an ACS resource in Azure.**
   - Azure Portal → Create resource → Communication Services. Same tenant as the D365 environment.
   - In the ACS resource: **Advanced Messaging** → **Channels** → **Register WhatsApp Business Account**. Embedded signup launches a Meta dialog that links the WABA to the ACS resource. **Meta Business admin must complete this step.**
   - Capture: ACS resource name, ACS connection string (primary key), Channel ID.
7. **Configure Event Grid** with Microsoft Entra app authentication. Event subscription on the ACS resource → endpoint will be the D365 webhook URL produced in the next step.
8. **In D365 Copilot Service admin center:** Channels → Messaging accounts → Add account → WhatsApp → Provider: Azure Communication Services. Paste ACS resource name, Channel ID, connection string, Entra app ID, Entra tenant ID. Copy the generated webhook URL back into the Event Grid subscription endpoint.
9. **Create a workstream** for the WhatsApp channel with routing rules.
10. **Set up message templates** (if outbound non-service messaging is planned). Templates are created in Meta Business Manager → WhatsApp Manager → Message Templates, then synced into the D365 workstream.

### 3.5 Pricing — Meta's per-message model (post-July 2025)

The old conversation-based model is gone. Meta now charges **per-message for template messages** by category. Service messages are free.

| Category | When it applies | Charged? |
|---|---|---|
| **Service** | Customer-initiated. The business replies within a 24-hour service window. Any free-form reply. | **Free** (no per-message charge by Meta; ACS $0.005/msg still applies) |
| **Utility** | Transactional/post-purchase: order confirmations, shipping updates, account alerts. Must use an approved template. | **Free** *if sent within an open customer-service window* (post-July 2025 change). Otherwise charged at the utility rate. |
| **Authentication** | OTPs and login codes. Must use an approved template. | Charged per message, country-specific. UK ~$0.0358 per message (Meta-published; check Meta WhatsApp pricing page for current rate). |
| **Marketing** | Promotional outbound. Must use an approved template. | Charged per message. UK ~$0.0691 per message (Meta-published; rate sheet updates quarterly). |

**Free entry points:** if a customer initiates from a **click-to-WhatsApp ad** or a **Facebook Page CTA button**, the next 72 hours of messages on either side are free of Meta charges (ACS usage fee still applies).

**For AWD's profile** (customer-initiated e-commerce inbound), the vast majority of conversations are **service-category and therefore Meta-free**. Expect monthly Meta-side charges to be small until you start sending marketing broadcasts.

### 3.6 Throttling — verified vs unverified WABA

| Tier | Daily outbound limit (unique customers initiated) | Requirements |
|---|---|---|
| **Tier 0 (unverified)** | 250/24h | Default for new WABAs without business verification |
| **Tier 1** | 1,000/24h | Business verification + approved display name |
| **Tier 2** | 10,000/24h | Sustained high-quality messaging |
| **Tier 3** | 100,000/24h | Continued high-quality messaging |
| **Tier 4** | Unlimited | Top tier |

AWD will land at **Tier 1** on day-one (verification already done). For a customer-initiated inbound use case this is more than enough — the throttle only applies to *business-initiated* conversations (outbound templates). Inbound is uncapped.

### 3.7 Approval gates summary

| Gate | Owner | Typical time |
|---|---|---|
| Meta Business Verification | Done | n/a |
| WhatsApp Business Account creation | Chris | 5 min |
| Phone number OTP verification | Tony (SIM holder) | 5 min if SIM is reachable |
| Display name approval | Meta review | 1–3 business days |
| ACS WhatsApp embedded signup | Chris (must be Business Admin) | 15 min |
| D365 channel + workstream config | Chris/Tarquin | 60 min |
| Template approvals (per template) | Meta review | <1 hour to 24 hours |

---

## 4. Recommendation per channel (for AWD's profile)

| Channel | Recommended path | Rationale |
|---|---|---|
| Facebook Messenger | **(a) Native D365 Facebook channel** | First-party, free at the connector layer, immediate. No reason to pay a CPaaS markup. |
| Instagram | **(a′) Native FB channel with Instagram linked to the FB Page** | Microsoft does not ship a standalone Instagram SKU. Linking IG to the FB Page is the supported pattern and uses the same webhook. Sprinklr only justifies itself at much larger volume. |
| WhatsApp | **(a) Native D365 WhatsApp via ACS** | First-party Microsoft path. One billing surface (Azure). ACS markup of $0.005/message is competitive with Twilio's ~$0.005 markup and avoids a separate vendor relationship. |

**Twilio is the fallback if ACS embedded signup fails for any reason** — same D365 admin experience, just a different provider dropdown. Worth knowing about, not worth picking by default.

---

## 5. Day 2 build sequence (recommended)

The right order is dictated by what's gated on Meta's review queue and Tony's SIM availability.

| Order | Channel | Day | Notes |
|---|---|---|---|
| 1 | Facebook Messenger — native | Day 2 (today) | Zero external dependencies. Get this live first — proves the D365 workstream/routing config works end-to-end and gives the reps something to handle. |
| 2 | Instagram — via FB Page link | Day 2 (today, immediately after FB) | Reuses the same FB app and webhook. Add `instagram_manage_messages` to the existing app-review submission. ~30 min of admin work. |
| 3 | **WhatsApp prep** (no end-user traffic yet) | Day 2 (today, in background) | Create the WABA, submit display name for approval, provision the ACS resource. These all proceed in parallel with FB/IG. |
| 4 | WhatsApp OTP + go-live | Day 3–5 | Gated on (a) Tony being reachable with the SIM, (b) Meta's display-name approval (1–3 days). When both clear, finish the embedded signup + D365 channel config in one sitting. |
| 5 | First template submission for WhatsApp | Day 5+ | Only needed when outbound messaging starts. Skip until there's a real broadcast use case. |

**Why defer WhatsApp:** the SIM-OTP plus display-name approval introduces 1–5 calendar days of dead time. Don't block FB/IG on it. Get the first two live, then close out WhatsApp asynchronously.

---

## 6. References

- [Configure a Facebook channel — Microsoft Learn](https://learn.microsoft.com/en-us/dynamics365/customer-service/administer/configure-facebook-channel)
- [Configure a WhatsApp channel through Azure Communication Services — Microsoft Learn](https://learn.microsoft.com/en-us/dynamics365/contact-center/administer/configure-whatsapp-acs)
- [Announcing D365 Contact Center WhatsApp channel powered by ACS (May 2025)](https://www.microsoft.com/en-us/dynamics-365/blog/it-professional/2025/05/01/announcing-dynamics-365-contact-center-whatsapp-channel-powered-by-azure-communication-services/)
- [Pricing for Advanced Messaging (ACS) — Microsoft Learn](https://learn.microsoft.com/en-us/azure/communication-services/concepts/advanced-messaging/whatsapp/pricing)
- [WhatsApp Business Platform pricing — Meta for Developers](https://developers.facebook.com/docs/whatsapp/pricing)
- [Dynamics 365 Contact Center pricing](https://www.microsoft.com/en-us/dynamics-365/products/contact-center/pricing)
- [WhatsApp display-name policy](https://developers.facebook.com/docs/whatsapp/overview/business-name-policy)
- [WhatsApp messaging limits / tiers](https://developers.facebook.com/docs/whatsapp/messaging-limits)
- [Sprinklr Service on Microsoft Dynamics 365](https://www.sprinklr.com/help/articles/microsoft-dynamics-365/sprinklr-service-on-microsoft-dynamics/633c5ca8a0522e093b06c1a6) (third-party reference only)
- [Sprout Social D365 integration](https://support.sproutsocial.com/hc/en-us/articles/360047117691-Microsoft-Dynamics-365-Integration) (one-way only)
