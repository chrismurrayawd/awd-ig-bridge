# Where this bridge fits — AWD Dynamics 365 Contact Center programme

Distilled context for the IG bridge build session. The full programme docs live in the AWD repo
at `.claude/reference/microsoft-integrations/d365-cc/` — this is the subset that matters for the
Instagram bridge. **Last distilled: 2026-05-23.**

## The programme in one paragraph

AWD is standing up **Dynamics 365 Contact Center** as the single agent desktop for voice, email,
and Meta channels (Facebook Messenger, Instagram DM, WhatsApp), plus a website chat widget. It runs
in the **AWD production Microsoft tenant**, pinned to **UK South** (data residency; fallback West
Europe). The build is phased: **Phase 1** lights up all channels on a 30-day trial; **Phase 2**
commits licensing and ports the live phone number; **Phase 3** adds intelligent routing + Power
Platform + a Copilot Studio deflection bot. The Instagram bridge is a **Phase 1 channel** — it lands
IG DMs in the same agent workspace as the other channels.

## Why the Instagram bridge is a separate build (not "just link IG to the FB Page")

The original connector map *assumed* Instagram would ride on the native D365 Facebook channel by
linking the IG account to the FB Page. **That was verified WRONG on 2026-05-23** three ways:

1. Microsoft's canonical [Overview of channels](https://learn.microsoft.com/en-us/dynamics365/customer-service/use/channels)
   doc lists supported social channels as **Apple / Facebook / WhatsApp / LINE only** — no Instagram.
2. Firsthand — there is no "Instagram" option in any D365 channel dropdown in the live `crm11` build.
3. Not on the 2025-W2 / 2026 release roadmap either.

IG messaging uses a **separate Instagram-object webhook + `instagram_manage_messages`**, which the
D365 Facebook connector (subscribed only to the Page `messages` field for Messenger) does not
consume. Linking IG to the FB Page surfaces IG DMs in the **Meta Business Suite** unified inbox, NOT
in the D365 agent workspace. → hence the **custom Direct Line bridge** this project builds.

Full detail + the rejected alternatives (SyncBox 365, Sprinklr, generic iPaaS) is in
[`meta-channel-connectors.md §2`](meta-channel-connectors.md).

## The Facebook App Review the IG bridge must sequence behind

AWD has a **Facebook `pages_messaging` App Review in flight** for the native FB Messenger channel.
Relevant AWD-specific state (as of 2026-05-21, from the programme's `official-docs.md` §AWD-state):

- FB app **"AWD Contact Center"**, App ID **`27031932926443539`**, business portfolio
  **Alloy Wheels Direct** (`605826456988689`).
- **Tech Provider = committed (irreversible)** 2026-05-21 — required to submit any permission to App Review.
- App Review **draft** `27043210481982450` open with `pages_messaging`, `pages_manage_metadata`,
  `pages_show_list`, `business_management`, `public_profile` — submission paused at the checklist.
- **Meta Business Verification = Verified** (2026-05-20).

**Why this matters for IG:** Meta reviews the **whole app**, one submission at a time, and a
submission is **irreversible once in review**. To keep IG's review clock independent of the FB
review, the IG bridge uses a **dedicated Meta app** (decided 2026-05-23) — not the FB app above.
The IG Live-mode App Review (`instagram_manage_messages` + `instagram_basic`) still submits **after
FB clears** (plan step 9), but on a *separate app* so the two don't collide.

**Dev-mode testing needs no App Review** — add Chris/Lachy/Tony as **app Testers** on the dedicated
IG app and they can DM the IG account immediately (plan step 7).

## The pattern to mirror — human-first routing

The AWD WhatsApp and Facebook channels route to a **human queue** (no bot). The IG bridge's D365
workstream should mirror that by default (plan open-decision #2 = human-first unless Chris opts to
front it with the Phase-3 deflection bot). The Phase-3 Copilot Studio deflection bot (BMF
fitment-checker + agent-assist) is a later upgrade, not a Phase-1 dependency.

## Azure home

- **Subscription:** Core Benefits Credits sub `95b2f141-…` — the **$2,400/yr Azure credit**
  (redeemed, expires **2027-03-26**). A small Function + Key Vault + App Insights costs a few £/mo,
  well inside the credit.
- **Region:** UK South (fallback West Europe). **RG:** e.g. `awd-contactcenter-rg`.

## Licensing (settled — for context only)

The D365 Contact Center side is covered by the **standalone CC Digital seat** AWD is buying at the
**$57/user/mo promo** (promo ends 30 Jun 2026; decision taken 2026-05-23). The custom Direct Line
channel needs **no extra D365 SKU** — it's covered by the Contact Center licence.

## Identity

All Microsoft/Azure/D365 admin: sign in as **`chris@chrismurray.eu`** (tenant Global Admin).
`alloywheelsdirect.com` is the mailbox/data domain, never the login.

## Source-of-truth pointers (in the AWD repo)

- Programme phasing + risks: `d365-cc/plans/phases-1-3-implementation.md`
- Connector option map: `d365-cc/reference/meta-channel-connectors.md` (copied here)
- Canonical MS/Meta docs + extracted facts: `d365-cc/reference/official-docs.md` (copied here)
- FB Phase-1 execution log (App Review state): `d365-cc/logs/phase1-execution-log-2026-05-20.md`
- Phase-3 AI deflection + voice routing: `d365-cc/plans/phase-3-ai-deflection-and-voice-routing.md`
