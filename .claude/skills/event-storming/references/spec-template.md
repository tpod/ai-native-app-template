# Document templates

Every element on the board gets an id so questions, changelogs and later levels can refer to it:
`E` event, `C` command, `A` actor, `X` external system, `P` policy, `R` read model, `K` constraint/aggregate, `H` hotspot, `O` opportunity, `V` value/risk, `BC` bounded context, `Q` open question.

Confidence values (always one of these):
`confirmed` (expert/user stated) · `evidenced` (internal source shows it; cite Sxx) · `industry` (research only) · `inferred` (deduced; say from what) · `assumed` (placeholder).

## Contents
1. STATE.md
2. 00-brief.md
3. 01-industry-research.md
4. 03-glossary.md
5. 04-spec.md (the Brandolini specification)
6. 05-open-questions.md
7. history/CHANGELOG.md

---

## 1. STATE.md

```markdown
# EventStorming: <topic>
- **Level:** Big Picture | Process Modeling (<context>) | Software Design (<context>)
- **Mode:** as-is | to-be
- **Spec version:** v<N> (<date>)
- **Last phase completed:** <0–6>
- **Next step:** <one concrete action, e.g. "Mine S03 (claims handbook) then rebuild spec v3">
- **Sources:** S01 ✅ mined · S02 ⏳ waiting for user · …
- **Open questions:** <n high> / <n total>
```

## 2. 00-brief.md

```markdown
# Brief: <topic>
- **Topic (user's words):**
- **Mode:** as-is / to-be
- **Starting level:**
- **Boundaries:** starts at "<first event>", ends at "<last event>"
- **Out of scope:**
- **Purpose / downstream use:**
- **Known experts / roles:**
- **Assumptions made by the AI:** (each one later becomes confirmed or a question)
```

## 3. 01-industry-research.md

```markdown
# Industry research: <topic>
_Hypothesis from public sources; not yet validated against the organization._

## Summary (5–8 lines)
## Canonical end-to-end process
<stages, with the usual pivotal events>
## Key terms
| Term | Meaning | Synonyms / regional variants | Source |
## Standards, regulations, obligations
| Obligation | Effect on the model (event, deadline, rule) | Source |
## Typical actors and external parties
## Typical subdomains (core / supporting / generic)
## Common pain points (hotspot and opportunity seeds)
## Reference event timeline
Chapter 1, <name>: Event A → Event B → **[Pivotal] Event C** → …
Chapter 2, …
## Sources
- [title](url)
```

## 4. 03-glossary.md

```markdown
# Ubiquitous language
| Term | Definition (in the business's words) | Bounded context | Synonyms / avoid | Industry term | Source(s) | Confidence |
```
One row per term *per context*. If a term means different things in two contexts, it gets two rows; that's expected in DDD.

## 5. 04-spec.md: the Brandolini specification draft

Include only the sections the current level uses (see notation.md §1). Big Picture usually omits §5–§7 detail; design level fills everything for its context.

```markdown
# EventStorming specification: <topic>
**Version:** v<N> · **Date:** <date> · **Level:** <level> · **Mode:** as-is/to-be · **Scope:** "<first event>" → "<last event>"

**Confidence mix:** confirmed n · evidenced n · industry n · inferred n · assumed n
**Top open hotspots:** H03, H07, H11 (see 05-open-questions.md)

## 1. Timeline (the wall)
Read left to right. Pivotal events in **bold** with 📌. Hotspots inline as ⚠️H<id>.

### Chapter 1: <name> (swimlane(s): <lanes>)
| # | Domain event | Triggered by | Actor / System | Policy reacting | Hotspots | Conf. | Source |
|---|---|---|---|---|---|---|---|
| E01 | Quote Requested | C01 Request Quote | A01 Prospect | P01 | | evidenced | S02 |
| E02 | 📌 **Contract Signed** | C04 Sign Contract | A02 Customer, X01 DocuSign | P03 | ⚠️H02 | confirmed | user 2026-11-24 |

### Temporal milestones
- "End of Month" → triggers P07

## 2. Story walk
**Forward:** <short prose, first event to last>
**Reverse:** <from last to first: "for X to happen, Y must have happened before…"; gaps found → H ids>

## 3. Actors
| Id | Actor / role | Issues commands | Needs read models | Conf. | Source |

## 4. External systems
| Id | System | Owned by | Receives commands | Emits events | Conf. | Source |

## 5. Commands
| Id | Command | Issued by (actor/policy) | Read model used | Handled by (K/X) | Can be rejected when… | Resulting event(s) | Conf. | Source |

## 6. Policies
| Id | Whenever (event) | Then (command) | Automated / manual (who) | Conf. | Source |

## 7. Read models
| Id | Name | Supports decision / command | Fields | Built from events | Conf. | Source |

## 8. Constraints / aggregates (design level)
### K01: [ ? ] (name postponed until stable)
- **Handles commands:** C05, C06
- **Emits events:** E09, E10
- **Preconditions:** …
- **Postconditions:** …
- **Invariants:** …
- **Conf. / source:** …

## 9. Bounded contexts (candidate)
| Id | Context | Events / commands it contains | Signal that suggests it (pivotal event / language shift / ownership) | Subdomain type (core/supporting/generic) | Owner | Conf. |

### Context relationships
- BC01 → BC02: <upstream/downstream, pattern if known: customer-supplier, conformist, ACL, published language, shared kernel, open-host>, via <events>

## 10. Value and risk
| Id | +/− | Description | Attached to | Source |

## 11. Opportunities
| Id | Opportunity | Attached to | Source |

## 12. Hotspots (summary)
| Id | Hotspot | Attached to | Question ref |
```

If a source cell needs several sources, list them: `S01, S04`. For `inferred`, write the reasoning briefly in the source cell ("inferred from S02 status enum").

## 6. 05-open-questions.md

```markdown
# Open questions & hotspots (spec v<N>)

## High priority
### Q01 · <type: causality gap | timeline gap | language conflict | source conflict | ownership | assumption | rule gap | risk>
- **Affects:** E07, C05, BC02
- **What we see:** <the observation, with evidence>
- **Why it matters:** <what breaks or is risky if we get this wrong>
- **Question for the expert:** <plain business language, answerable in a sentence or two>
- **Ask:** <role/person>
- **Status:** open | answered (<answer, who, date>) | parked

## Medium priority
…
## Low priority
…
## Resolved (kept for traceability)
…
```

## 7. history/CHANGELOG.md

```markdown
| Version | Date | Trigger | Changes |
|---|---|---|---|
| v2 | 2026-11-24 | Mined S02 (orders schema); answers to Q01–Q03 | +4 events, renamed "Order Closed" → "Order Fulfilled" (Q02), split BC01 into Sales / Fulfilment |
```
