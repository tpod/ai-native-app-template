# Mining DDD concepts from internal sources

Each source type hides the domain in a different place. Use the heuristics for the source type, then fill in the extraction template at the end. Always record **evidence** (table.column, file:line, doc section, short quote). An unverifiable claim is worth little to the user.

## Contents
1. Database schemas, migrations, data dictionaries
2. Application code
3. Event / message / API schemas
4. Workflow and job definitions
5. Wikis, manuals, handbooks, SOPs, policy docs
6. Tickets, incidents, spreadsheets, reports
7. Extraction template (per source)

## 1. Database schemas, migrations, data dictionaries

| Look for | Suggests |
|---|---|
| Timestamp columns `*_at`, `*_date` (`approved_at`, `shipped_on`) | A domain event: "X Approved", "X Shipped" |
| Status/state enums and their values | A lifecycle; each transition is a candidate event. Map the state machine |
| `*_by` / `*_user_id` next to a timestamp | The actor of that event |
| History / audit / log / `*_events` tables | Real event names already in use, often the best evidence |
| Tables with their own lifecycle and strong FK ownership (cascade delete, composite keys) | Aggregate/entity boundaries |
| Tables with no identity used by value (address, money, period) | Value objects |
| Unique constraints, check constraints, triggers | Invariants → constraints on the aggregate |
| Join tables carrying data (`enrollment`, `assignment`) | A domain concept that deserves a name |
| Same concept in two schemas with different columns | Two bounded contexts, or a duplication hotspot |
| `notes`, `comments`, `extra_json`, `flag1`, nullable columns everyone fills manually | Hidden rules or workarounds → hotspot |
| External ids (`stripe_id`, `erp_ref`) | External systems |
| Migration history | When rules/concepts changed; old columns show abandoned concepts |

Translate data names into business language. `ord_sts = 4` is not an event, but "Order Dispatched" might be. Mark the translation `inferred` unless documented.

## 2. Application code

| Look for | Suggests |
|---|---|
| Method/handler names with intent (`approveClaim`, `cancel_order`) | Commands |
| Code after a successful state change (sending email, publishing, calling services) | Events plus policies ("whenever ... then ...") |
| Guard clauses, validations, `throw` / `raise` with business messages | Invariants and preconditions → constraints |
| Classes that own collections and enforce rules on them | Aggregates (note: the code's boundaries may be accidental) |
| Event classes, `publish`/`emit`/`dispatch` calls | Explicit domain events (check they are business facts, not technical) |
| Schedulers, cron, `@Scheduled`, sweepers | Time-triggered policies ("whenever end of day ...") |
| HTTP/SDK clients | External systems |
| Controllers/screens and the fields they load | Read models, actors (via roles/permissions) |
| Roles/permissions/authorization checks | Actors and who may issue which command |
| Module/package/service boundaries, different names for the same thing across modules | Candidate bounded contexts, language shifts |
| `TODO`, `HACK`, feature flags, special-case branches for one customer | Hotspots |

For large repos: start from routes/handlers, domain/model folders, event definitions and schedulers; don't read everything.

## 3. Event / message / API schemas (Kafka, Avro, AsyncAPI, OpenAPI, GraphQL)

- Topic/message names → events (check tense and business meaning); POST/PUT/PATCH operations → commands; GET → read models.
- Producers and consumers → which context owns an event and who reacts (policies, context relationships).
- Payload fields → what the event carries; missing fields consumers need indicate coupling.

## 4. Workflow and job definitions (BPMN, Camunda, Temporal, Step Functions, Airflow, Zapier)

- Tasks → commands; user tasks → actor + read model; service tasks → system or policy.
- Gateways/branches → business rules and alternative event paths (failure, rejection).
- Timers → time-triggered events/policies. Compensation/error handlers → compensating events.

## 5. Wikis, manuals, handbooks, SOPs, policy documents

| Look for | Suggests |
|---|---|
| "When ..., the ... must ..." / "If ..., then ..." | Policies |
| "must", "may not", "only if", "within N days", limits, thresholds | Constraints, deadlines, temporal milestones |
| Role names, team names, approval chains | Actors, swimlanes, ownership boundaries |
| Step lists in procedures | Command → event chains; check each step's outcome |
| Forms, checklists, required fields | Read models (what the person needs to decide) |
| Glossaries and defined terms | Ubiquitous language (note exact wording) |
| "Contact X", "escalate to", "manually check" | Manual policies, handoffs, hotspots |
| Exceptions and edge cases sections | Alternative and failure paths |
| Document date/owner | How current it is. Stale docs vs. code is a conflict hotspot |

Documents describe how things *should* work; code and data show how they *do*. Record both, and flag the difference.

## 6. Tickets, incidents, spreadsheets, reports

- Recurring ticket themes → hotspots and risks (with frequency if visible).
- Incidents → missing invariants or failure paths.
- Spreadsheets → shadow systems: treat as external systems, and their columns as read models nobody built.
- Report definitions/KPIs → read models and what the business values (value stickies).

## 7. Extraction template (per source)

Write one file per source, `02-sources/Sxx-<name>.md`:

```markdown
# Sxx: <source name>

- **Type:** schema | code | api/event schema | workflow | wiki/manual | tickets | spreadsheet | other
- **Location:** <path / URL / "pasted by user">
- **Date / owner / version:** <if known>
- **Scope covered:** <which parts of the flow this source speaks to>
- **Reliability notes:** <stale? partial? describes intended vs. actual behavior?>

## Domain events
| Candidate event (past tense) | Trigger (command / policy / system / time) | Evidence | Confidence | Notes |

## Commands
| Command (imperative) | Issued by (actor/policy) | Evidence | Confidence |

## Actors & roles
| Actor | What they do | Evidence |

## External systems
| System | Interaction | Evidence |

## Policies
| Whenever ... | Then ... | Automated / manual | Evidence |

## Read models
| For decision/command | Fields | Evidence |

## Constraints / invariants / rules
| Rule (pre/post/invariant) | Protects | Where enforced | Evidence |

## Entities, value objects & lifecycles
- <entity>: states A → B → C (evidence)

## Terms (exact wording in this source)
| Term | Meaning here | Industry/other-source equivalent | Evidence |

## Context-boundary signals
- <language shift, ownership change, separate store, separate team>

## Conflicts, gaps, workarounds → hotspot candidates
- <what conflicts with research or other sources, what is implied but missing, what looks like a workaround>
```

Leave a section out if the source has nothing for it. Don't invent content to fill a table.
