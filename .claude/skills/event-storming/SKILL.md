---
name: event-storming
description: Use when discovering or modeling a business domain or process before building it (EventStorming, Brandolini, DDD big picture / process / design-level, bounded contexts, domain events, aggregates), e.g. "model our claims process" or "map onboarding before we build it", or when resuming an event-storming workspace in docs/event-storming. Researches industry terms, mines internal material (schemas, code, wikis, manuals) and iterates a Brandolini specification draft with open questions, then hands design-level output to add-aggregate and add-action.
---

# EventStorming workflow (DDD)

You play two roles at once: **researcher** (you gather and mine material) and **facilitator** (you keep the model honest, surface conflicts, and ask the questions the domain experts must answer). The user and their colleagues are the domain experts. You do not have the last word on the domain; the experts do. So every element you write down carries where it came from and how sure you are, and anything you guessed is a question for the user, not a fact.

The workflow is a loop, not a pipeline. Each pass makes the spec more complete and more certain:

```
0 Frame → 1 Industry research → 2 Ask for internal material → 3 Mine each source
      → 4 Synthesize spec draft → 5 Review: gaps, hotspots, questions
      → 6 Fold in answers / new material → back to 3/4/5 …  → zoom in a level
      → 7 Hand off a design-level context to the build skills
```

## The workspace

All work lives in files, so a session can stop and resume and the user can read and edit everything. Default location: `docs/event-storming/<topic-slug>/` in this repo, next to the other design docs in `docs/` (ask only if the user wants it elsewhere). Commit it like any other design doc.

```
docs/event-storming/<topic-slug>/
├── STATE.md                    # where we are: phase, level, spec version, next step, source list
├── 00-brief.md                 # the framing (Phase 0)
├── 01-industry-research.md     # Phase 1
├── 02-sources/
│   ├── INDEX.md                # every internal source: id, type, location, status
│   └── S01-<name>.md …         # one extraction per source (Phase 3)
├── 03-glossary.md              # ubiquitous language, kept in sync with the spec
├── 04-spec.md                  # the current Brandolini specification draft
├── 05-open-questions.md        # hotspots, gaps, questions, numbered and tracked
└── history/
    └── spec-v1.md …            # snapshot of each previous spec version + CHANGELOG.md
```

**On start:** if a workspace for the topic already exists, read `STATE.md` first and continue from the recorded next step instead of starting over. Tell the user in one or two lines where things stand.

**After every phase:** update `STATE.md` (phase done, next step, spec version) so the next session can pick up exactly here.

## Reference files (read when you reach the phase that needs them)

- `references/notation.md`: the sticky-note grammar, the three levels, and Brandolini's "picture that explains everything". Read before Phase 4 the first time.
- `references/source-extraction.md`: how to mine DDD concepts from each kind of internal material (schemas, code, logs, wikis, manuals, forms, tickets). Read before Phase 3.
- `references/spec-template.md`: the exact structure of `04-spec.md` and the other documents. Read before writing any of them.

---

## Phase 0: Frame the session

Ask the user, in one compact message (skip anything they already told you):

1. **Topic**: the business flow, idea or domain, in their words.
2. **As-is or to-be?** Mapping how things work today, or designing a new service or process? This changes what counts as a gap: as-is gaps are missing knowledge, to-be gaps are undecided design.
3. **Level to start at**: Big Picture (whole line of business, end to end), Process Modeling (one workflow), or Software Design (one bounded context, toward code). Recommend Big Picture unless the scope is already narrow, because zooming in too early hides context boundaries.
4. **Boundaries**: where the story starts and ends (the first and last event), and what is out of scope.
5. **Purpose**: what the output feeds into (new system, rewrite, process fix, onboarding, vendor selection). This decides which hotspots matter.

If the user just says "go", make sensible assumptions, write them in the brief as assumptions, and continue. Write `00-brief.md` and `STATE.md`.

## Phase 1: Industry research

Goal: give the model an outside-in vocabulary and a reference process, so internal material can be compared against how the industry normally works. This is what an experienced facilitator brings into the room.

Use web search (several queries in one go; the topic name plus "process", "lifecycle", "glossary", "standard", "regulation", plus the relevant standards bodies). Look for:

- **Canonical lifecycle / end-to-end process** (e.g. order-to-cash, procure-to-pay, claims lifecycle, KYC onboarding) and its usual stages.
- **Industry terms and their precise meanings**, including synonyms and terms that differ by region or vendor.
- **Standards, regulations and compliance obligations** that create mandatory events, deadlines or rules (these become constraints, policies and risk stickies).
- **Common external parties and systems** (payment providers, registries, carriers, regulators).
- **Known pain points** in the industry (these seed hotspots and opportunities).
- **Typical subdomains** and which ones are usually core vs. supporting vs. generic.

Keep it short: research enough to have a solid reference, not a thesis. Cite sources by URL. Write `01-industry-research.md` per the template, ending with a **reference event timeline** (past-tense events, left to right, grouped by pivotal events). This is a hypothesis, marked as such, and everything in it is confidence `industry`.

## Phase 2: Ask for internal material

Internal material is where the real domain is hiding. Ask the user for it specifically, because people undersell what they have. Offer a checklist suited to the topic, for example:

- **Data**: database schemas / DDL, ER diagrams, migrations, sample rows, data dictionaries, event or message schemas (Kafka topics, Avro/JSON schemas), API specs (OpenAPI, GraphQL).
- **Code**: repository paths, service/module names, domain or model folders, state machines, workflow definitions (BPMN, Camunda, Temporal, Step Functions), cron jobs and batch jobs.
- **Documents**: wiki/Confluence pages, process docs, handbooks, manuals, SOPs, training material, policy documents, contracts/SLAs.
- **Operational**: support tickets, incident reports, audit logs, status/enum lists, report definitions, spreadsheets people use as shadow systems.
- **People**: who the experts are per area (to point open questions at the right person).

Accept files, paths, pasted text, URLs, or repo locations. For each item, add a row to `02-sources/INDEX.md` with an id (`S01`, `S02`, …). If the user has nothing yet, say what is lost (everything stays `industry` / `assumed` confidence), and continue to Phase 4 on research alone.

Material can arrive at any time later; each new source just goes through Phase 3 and then triggers a new spec version.

## Phase 3: Mine each source

Process sources one at a time, each into its own `02-sources/Sxx-<name>.md`, following `references/source-extraction.md`. Work this way because sources contradict each other, and the contradictions are some of the most valuable findings. Merging too early hides them.

For each source extract candidate: domain events, commands, actors, external systems, policies, read models, constraints/invariants, entities and value objects, state lifecycles, terms (with the exact wording the source uses), and signals of context boundaries. Every item gets an **evidence pointer** (table/column, file:line, page/section, quote) so the user can verify it.

Then note: terms that clash with the industry research or other sources, things the source implies but doesn't show, and anything that looks like a workaround (shadow spreadsheets, "manual" statuses, free-text fields that carry meaning; these are often where hotspots live).

**This repo as a source.** When the app built on this template already holds part of the domain, mine it too (it is the most precise source there is): `[Action]` classes in `src/AppTemplate.Api/Features/` are commands, `[Query]` classes are read models, `[Event("<aggregate>.<verb>", 1)]` records in the Domain's `*Events.cs` files are domain events, the checks in aggregate behavior methods and the `[Error]` records in `*Errors.cs` are invariants and preconditions, `IDomainEventHandler<TEvent>` implementations are policies, and the rows that `get_history` returns are the real event log, including who did what and why (`reason`). The Projects example in `src/AppTemplate.Domain/Projects` is template sample code, not the user's domain, so skip it.

For large codebases or many sources, mining sources in parallel with subagents works well. Give each one the source, `references/source-extraction.md`, and the output template, and merge nothing yourself until all are back.

## Phase 4: Synthesize the specification draft

Read `references/notation.md` and `references/spec-template.md` first. Then build `04-spec.md` at the current level:

1. **Timeline first.** Merge all candidate events into one chronological flow of past-tense domain events. Keep the sorting anchors: pivotal events, swimlanes, temporal milestones. Don't delete duplicates hastily. The same name in two places may be two bounded contexts; record that as a hotspot rather than merging.
2. **Layer the other notation** for the level you're at (Big Picture: actors, systems, hotspots, opportunities, value/risk; Process: plus commands, policies, read models; Design: plus constraints/aggregates with pre/postconditions and invariants).
3. **Every element carries provenance and confidence**: `confirmed` (user/expert said so), `evidenced` (source shows it, cite it), `industry` (from research only), `inferred` (you deduced it; say from what), `assumed` (placeholder to unblock). Low-confidence elements stay on the board; they're just visibly flagged. This is what lets the user see at a glance what's solid.
4. **Name things in the domain's language.** Prefer the term experts and internal docs actually use over the industry term, and record the mapping in `03-glossary.md`. When terms collide, keep both and add a hotspot.
5. **Bounded contexts emerge, they aren't imposed.** Propose candidate contexts around pivotal events, language shifts, and ownership handoffs, and say which signal drove each one.
6. **Constraints are named last.** At design level, describe the rules first and leave the aggregate name blank (`[ ? ]`) until the commands, events and invariants grouped around it are stable (Brandolini's blank-naming).
7. **Narrate it.** Write the "story walk": a short prose walk-through from first to last event, then a reverse walk from last to first asking "what must have happened before this?". Wherever the story stumbles, add a hotspot. Doing both directions is what surfaces missing events.

Before overwriting `04-spec.md`, copy the previous version to `history/spec-vN.md` and add a line to `history/CHANGELOG.md` saying what changed and why.

## Phase 5: Review: what's uncertain, missing, or conflicting

This is the facilitator's job of putting hotspots on the wall. Go through the spec systematically and write `05-open-questions.md`. Check for:

- **Gaps in causality**: an event with no trigger (no command, policy, external system or time); a command with no actor or policy; a policy with no resulting command; a human command with no read model (how does the person decide?).
- **Timeline gaps**: the reverse narrative couldn't explain what came before; missing failure/compensation paths (what happens when it's rejected, cancelled, times out, is partially done?); missing end states.
- **Language conflicts**: one term, two meanings; two terms, one meaning; industry term vs. internal term.
- **Source conflicts**: schema says X, wiki says Y, code does Z.
- **Ownership and boundary doubts**: who owns this decision/data? Is this one context or two?
- **Unvalidated assumptions**: everything `assumed` or `inferred` that matters.
- **Rules without enforcement / enforcement without rules**: code checks nobody documented, or policies nobody implemented.
- **Risks and opportunities**: compliance exposure, manual steps, double data entry, bottlenecks.

Each item gets: id (`Q01`…), type, the affected spec element(s), why it matters (what goes wrong in the model or the system if it's wrong), a concrete question phrased for a business expert (no jargon), suggested person/role to ask, and a priority (high = blocks the model or carries real risk).

Then **present to the user** a short summary: the spec version, the confidence mix, and the top 5–8 questions by priority. Don't dump the whole list in chat; it's in the file. Ask them to answer what they can, point to more material, or mark items "unknown, park it". Experts dot-vote in a real workshop; here, ask the user which hotspots matter most.

## Phase 6: Fold in answers and iterate

When the user answers or adds material:

- Answers → update the affected elements (confidence becomes `confirmed`, cite "user, <date>" or the named expert), mark the question resolved with the answer, and propagate to the glossary.
- New material → Phase 3 for that source, then Phase 4.
- Bump the spec version, snapshot the old one, write the changelog line, rerun Phase 5 (new answers usually create new questions; that's normal).

**When to zoom in a level:** when the Big Picture timeline is stable (few high-priority timeline hotspots left) and candidate contexts are agreed, suggest taking one bounded context to Process Modeling, and later Software Design. Copy only that context's events to a new section/file (`04-spec-<context>.md`) and continue the same loop there. Ask the user which context first. Usually it's the core domain or the one with most hotspots.

**When to stop:** the user's purpose (Phase 0) is served: e.g. contexts and language are clear enough to plan a system, or aggregates with invariants are clear enough to code. Say so, and summarize what remains open.

## Phase 7: Hand off to the build skills

When a bounded context reaches Software Design level and its constraints are named, the spec maps one to one onto this template. Offer to start building, one aggregate at a time, and keep the spec ids in the plan so code and spec stay traceable.

| Spec element | Template construct | Skill |
|---|---|---|
| Constraint / aggregate (K) | `AggregateRoot<TId>` with behavior methods returning OneOf | `skill:add-aggregate` |
| Precondition / invariant on K | A check in the behavior method returning an `AppError` record with `[Error("<area>.<problem>", ErrorKind.X, …)]`; Conflict for state clashes, Precondition for stale assumptions | `skill:domain-modeling` |
| Command (C) | An `[Action("snake_name", …)]` mutation, previewed then executed | `skill:add-action` |
| Domain event (E) | A past-tense `sealed record` with `[Event("<aggregate>.<verb>", 1)]`: "Invoice Issued" becomes `invoice.issued` | `skill:domain-modeling` |
| Read model (R) | A `[Query]` returning a DTO with exactly the fields the spec lists | `skill:add-action` |
| Policy (P), inside one context | An `IDomainEventHandler<TEvent>` that issues the next command | `skill:domain-modeling` |
| Value object (from rules on a field) | A `sealed record` with `Create` returning `OneOf<TVo, ValidationError>` | `skill:domain-modeling` |
| Actor (A) | Who may call the action (authorization) and the UI page they use | `skill:frontend-feature` |
| Read model with a UI mockup | A page using `<DataTable>` and `<PreviewConfirmDialog>` | `skill:frontend-feature` |

Two places where the spec and the template can disagree, so raise them as hotspots instead of quietly bending one to fit the other:

- **Policies across contexts or with external systems.** Event handlers here run in-process, after commit and best effort, with no outbox. A policy whose failure costs money or breaks compliance needs something more reliable; flag it.
- **Aggregate size.** A constraint whose invariants span many things often means two aggregates, or a rule that only needs eventual consistency. Each action loads and saves one aggregate, so the spec's transactional boundaries must match.

Write the tests first for each action, per `skill:tdd`, using the spec's preconditions and invariants as the error cases.

---

## Working principles

- **Facilitator, not designer.** Surface options and conflicts; don't silently resolve them. If you have a recommendation, label it as one.
- **Don't stall on disagreement.** Put a hotspot on it and keep moving, just as in the room.
- **No premature technology.** Keep databases, frameworks and queues out of the spec until design level, and even then only as external systems or implementation notes. Event names describe business facts ("Invoice Issued"), not technical ones ("Row Inserted", "Message Published").
- **Grammar is not cosmetic.** Past-tense events and imperative commands force the right question ("what actually happened?" vs. "what did someone intend?"). Fix violations rather than tolerating them.
- **Show your evidence.** The user must be able to check any claim against its source in seconds.
- **Keep chat short.** Documents hold the detail; chat holds the status, the decisions you need, and links to the files.

## Optional: a visual board

If the user wants to see the wall, render the current spec as a color-coded board (orange events, blue commands, yellow actors, lilac policies, green read models, pink systems, pale-yellow constraints, tilted neon-pink hotspots) left to right in swimlanes, as an HTML page or Mermaid diagram, using the colors in `references/notation.md`. Offer it after the first spec version.
