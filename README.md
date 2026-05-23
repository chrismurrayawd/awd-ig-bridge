# awd-ig-bridge

**Instagram Direct → Dynamics 365 Contact Center bridge.** A .NET Azure Function that relays
Instagram DMs into the D365 agent workspace via Direct Line API 3.0 ("bring your own channel"),
forked from Microsoft's reference connector.

> **Status: bootstrap / not yet built.** This folder currently holds the implementation plan and
> the context it needs. The .NET solution is created by **build step 1** (fork the MS sample). See
> the plan.

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

The .NET solution (`src/` etc.) does not exist yet — **the first build step is to fork Microsoft's
sample into this folder** and start replacing the MessageBird adapter with an Instagram adapter.
See `CLAUDE.md` → "First moves".

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
