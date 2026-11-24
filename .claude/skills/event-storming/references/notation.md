# EventStorming notation and levels

Sources: Alberto Brandolini, *Introducing EventStorming* (Leanpub); DDD Crew, *EventStorming Glossary & Cheat Sheet* (github.com/ddd-crew/eventstorming-glossary-cheat-sheet).

## Contents
1. The three levels
2. Sticky-note grammar (element table)
3. The picture that explains everything
4. Sorting anchors
5. Big Picture facilitation sequence (mirrored by the AI workflow)
6. Process / Design level sequence
7. Grammar rules and common mistakes
8. Board colors for rendering

## 1. The three levels

| Level | Goal and scope | Notation in play | Main DDD output |
|---|---|---|---|
| **Big Picture** | Assess a whole line of business, explore a new business model, map an end-to-end domain | Domain events, hotspots, opportunities, pivotal events, swimlanes, actors, systems, value/risk | Emerging bounded contexts, core vocabulary, prioritized hotspots |
| **Process Modeling** | One workflow: find bottlenecks, handoffs, decoupling points | Adds commands/actions, policies, read models, systems in strict causal chains | Validated process flows, context-mapping patterns, candidate aggregates |
| **Software Design** | Event-driven behavior inside one bounded context | Adds constraints (aggregates), explicit business rules, UI mockups | Aggregates with invariants, commands, events, policies, read-model projections |

Move down a level only once the level above is stable for the part you zoom into.

## 2. Sticky-note grammar

| Element | Sticky | Writing convention | Role |
|---|---|---|---|
| **Domain Event** | Orange | Past tense: "Order Placed" | Core building block: something domain-relevant that happened, triggered by a command, external system, passage of time, or another event |
| **Hotspot** | Neon pink / red / maroon, tilted | Question, conflict, friction | Captures inconsistencies, dissent, deferred questions without stalling the flow |
| **Opportunity** | Green | Improvement or simplification | Counterweight to hotspots, added once the timeline stabilizes |
| **Command / Action** | Blue | Imperative present: "Place Order" | A decision or intent issued by an actor or policy. "Action" reads better for non-technical people |
| **Actor / Agent** | Small yellow | Person, role or team | Who initiates commands or interacts around events |
| **System / External System** | Wide pink | System name: "Payment API", "Excel" | Deployable IT systems or dependencies outside the model's ownership |
| **Policy (Reactor)** | Lilac / purple | "Whenever X, then Y" | Reactive rule (automated or manual) connecting an event to a following command |
| **Read / Query Model** | Green | List of data fields | Information an actor needs to make a decision and issue a command |
| **UI Mockup** | White | Rough sketch | Optional, design level: how read-model data appears on screen |
| **Constraint / Aggregate** | Big pale yellow | Business rules; name postponed | Enforces transactional invariants between command and event. "Constraint" is friendlier to business experts |
| **Value / Risk** | Small green (+) / red (−) | Benefit or loss/penalty | Shows value creation and financial/operational risk along the flow |
| **Pivotal Event** | Orange + yellow tape | Past tense | Major business transition; boundary between phases, often between contexts |
| **Bounded Context** | Boundary drawn around clusters | Context name | Where a model and its language are consistent |

Green is used for both opportunities and read models on a physical wall. In documents always label them explicitly.

## 3. The picture that explains everything

```
          ┌──────────────┐
          │  Read Model  │◄──────────────────── updates ──────────────┐
          └──────┬───────┘                                             │
       informs   │                                                     │
          ┌──────▼──┐  issues  ┌─────────┐ invoked on ┌─────────────┐  │  ┌──────────────┐
          │  Actor  ├─────────►│ Command ├──────────►│ Constraint / │──┼─►│ Domain Event │
          └─────────┘          └────▲────┘           │ Aggregate or │  │  └──────┬───────┘
                                    │                │ Ext. System  │  │         │
                                    │ issues         └─────────────┘  │         │ triggers
                              ┌─────┴────┐                             │         │
                              │  Policy  │◄────────────────────────────┴─────────┘
                              └──────────┘   "Whenever <event>, then <command>"
```

Read it as a sentence: *an Actor looks at a Read Model and issues a Command; the Command goes to a Constraint (aggregate) or an External System, which produces a Domain Event; the event updates Read Models and triggers Policies, which issue further Commands.*

Every event must be explainable by one of: a command (from an actor or policy), an external system, or time passing ("Month Ended"). If none applies, that's a hotspot.

## 4. Sorting anchors

- **Pivotal events**: the handful of events where the business changes phase (e.g. "Contract Signed", "Order Shipped"). Use them to chunk the timeline.
- **Swimlanes**: parallel horizontal lanes for departments or parallel processes.
- **Temporal milestones**: "End of Month", "Day 30", "Renewal Date".
- **Chapters**: named sections between pivotal events.

## 5. Big Picture sequence (what a human workshop does; the AI workflow mirrors it)

1. Check-in and working agreements: everyone is right, conversations deepen relationships, we learn together.
2. **Chaotic exploration**: everyone writes past-tense events silently, roughly left to right. *(AI: candidate events from each source, kept separate.)*
3. **Enforce the timeline**: merge into one chronological flow, no arrows, don't rush to delete duplicates. *(AI: Phase 4 step 1.)*
4. **Sorting anchors**: pivotal events, swimlanes, milestones.
5. **People and systems**: actors, systems, opportunities, value/risk.
6. **Explicit walkthrough** start to end, then **reverse narrative** end to start, adding hotspots where narrators hesitate. *(AI: story walk.)*
7. **Emerging bounded contexts** around pivotal events, language shifts, ownership handoffs.
8. **Dot voting** on hotspots/opportunities, assign owners. *(AI: user prioritizes open questions.)*

## 6. Process modeling and software design sequence

1. Copy only the scoped context's events onto a fresh board.
2. Put **commands** directly left of the events they cause.
3. Put an **actor** or a **policy** left of each command: is it a human decision, time, or an automated rule?
4. Put **read models** (and UI mockups) between the previous event and human commands: what exact data does the person need?
5. Put **external systems** where commands leave the boundary or third parties send events in.
6. Put **constraints** (big pale yellow) between commands and events, **unnamed**.
7. On each constraint write **preconditions** (must be true before), **postconditions** (true after), **invariants** (always true).
8. Name aggregates only when grouped commands, events and invariants stabilize. Export with remaining hotspots.

## 7. Grammar rules and common mistakes

- Event = business fact, past tense, from the domain's perspective: "Payment Received", not "Payment", "Receive Payment", "Payment Status Updated" or "Record Saved".
- Avoid CRUD events ("Customer Updated"). Ask *why* it changed: "Customer Relocated", "Credit Limit Raised".
- Command = imperative, one intent: "Approve Claim", not "Claim Approval Process".
- Commands can be rejected; events cannot be undone (only compensated by later events: "Order Cancelled").
- Policy = "Whenever <event> then <command>". If a human applies it, note the role. Hidden manual policies ("Anna checks the list every morning") are prime hotspots.
- Read model = concrete fields, not "the dashboard".
- Don't name an aggregate after a table or a UI screen. Name it after the consistency it protects.
- Status enums are not events, but each status transition usually *is* one.

## 8. Board colors for rendering

| Element | Hex |
|---|---|
| Domain event | `#FF9F1C` |
| Pivotal event | `#FF9F1C` with `#FFE600` border/tape |
| Command | `#4DA3FF` |
| Actor | `#FFF176` (small) |
| Policy | `#C39BD3` |
| Read model | `#81C784` |
| Opportunity | `#A5D6A7` |
| External system | `#F48FB1` (wide) |
| Constraint / aggregate | `#FFF59D` (large) |
| Hotspot | `#FF1F7A` (rotated ~-6°) |
| UI mockup | `#FFFFFF` with border |
| Value + / Risk − | `#43A047` / `#E53935` (small) |
