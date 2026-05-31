New hardening item — found during D365 Contact Center concurrency testing (2026-05-31):

BUG: When an Instagram DM arrives while the D365 agent (cs@alloywheelsdirect.com) is on a
voice call — i.e. agent presence = "Busy - DND" — the bridge causes an immediate incoming-
conversation TOAST / assignment to the agent, interrupting the call.

By contrast, the native D365 channels (WhatsApp, Facebook, live web chat) correctly QUEUE the
message as an open work item with NO toast while the agent is DND, and only surface it once the
agent is Available again.

I verified on the D365 side that all four messaging workstreams have IDENTICAL routing config —
same allowed presences (192360000=Available, 192360001=Busy; DND 192360002 excluded), same
work-distribution mode, same capacity model. So the difference is NOT a D365 setting. It's the
DELIVERY PATH: Instagram is the only channel coming through this custom bridge, and the bridge
appears to be injecting/assigning the conversation directly to the agent rather than dropping the
inbound message into the AWD Instagram workstream's QUEUE and letting D365 unified routing assign
it (which is what respects presence + capacity).

DESIRED BEHAVIOUR: IG inbound messages should enter via the AWD Instagram workstream's queue /
unified routing, so they obey the same presence + capacity gating as the other channels — i.e.
while the agent is on a call (Busy-DND), the IG conversation waits silently in the queue (open
work item, no toast) and is offered only when the agent returns to Available/Busy.

PLEASE:
1. Find where the bridge hands an inbound IG message to D365 (Direct Line channel? direct
   msdyn_ocliveworkitem creation? direct agent assignment / queue bypass?).
2. Change it so the conversation is routed via the AWD Instagram workstream's queue (unified
   routing) instead of being directly assigned — so presence/capacity rules apply.
3. Add it as a hardening item.

D365 CONTEXT:
- Org: org7a63d391.crm11.dynamics.com
- AWD Instagram workstream id: ae0c4a33-8876-b07a-7b08-cca1ebafdc78
  (capacity 10 units; allowed presences "192360000,192360001")
- Agent: cs@alloywheelsdirect.com
- Presence codes: 192360000 Available, 192360001 Busy, 192360002 Do-Not-Disturb (auto-set on a call)

ACCEPTANCE TEST: Put cs@ on a voice call (presence → Busy-DND). Send an IG DM. Expected: it appears
as a queued / open work item with NO toast, and is offered to the agent only after the call ends.
This should match the WhatsApp / live-chat behaviour.
