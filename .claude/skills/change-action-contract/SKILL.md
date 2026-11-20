---
name: change-action-contract
description: Use when changing an existing action's or query's name, input record, output DTO, error codes, description or Destructive flag. These are MCP tool and HTTP contracts that runtime agents and the web client depend on.
---

# Change an action's contract

Every `[Action]`/`[Query]` is published three ways from the same class: MCP tools (`preview_<name>` and `<name>`, or `<name>` for a query), HTTP operations (`/api/actions/<name>/preview`, `/api/actions/<name>`, `/api/queries/<name>`) and the generated TypeScript client. Runtime agents cache tool lists and prompts that mention tool names and fields. A breaking change fails them silently, so prefer additive changes.

## What the contract is

| Part | Comes from |
|---|---|
| Tool and route name | the `name` in `[Action("name", …)]` / `[Query("name", …)]` |
| Tool description | the attribute description, plus "Possible error codes: …" and the preview/confirmation text (`src/AppTemplate.Api/Mcp/ToolDescriptions.cs`) |
| Input schema | the input record, through `JsonSchemaExporter` with camelCase names and snake_case enum strings (`src/AppTemplate.Api/Mcp/McpToolFactory.cs`) |
| Output | the type returned by `ToResult` (mutations) or `Run` (queries), usually a DTO such as `ProjectDto` in `src/AppTemplate.Api/Features/Projects/ProjectDtos.cs` |
| Error codes | the OneOf error arguments of `Load` + `Apply` (or `Run`), plus `validation.failed` when a validator exists |
| Field error codes | VO `ValidationError` codes and `.WithErrorCode(...)` values inside `validation.failed` |
| Hints | `Destructive = true` sets MCP `destructiveHint`; queries are `readOnlyHint` |

The OpenAPI snapshot `src/web/openapi.json` shows all of this except the MCP tool descriptions. Its diff is the review artifact for every contract change.

## Additive or breaking

| Change | Kind | Do |
|---|---|---|
| New optional input field (nullable, or with a default the handler applies) | Additive | Fine. Describe the default in the action description |
| New output field | Additive | Fine |
| New action or query | Additive | `skill:add-action` |
| Description wording that keeps the meaning | Additive | Fine |
| New error code | Additive for HTTP, but a new case for agents | Add a test, and give it a suggestion an agent can act on without help |
| Removing an error code that can no longer happen | Additive | Remove its `[Covers]` test too |
| Rename a tool, input field or output field | **Breaking** | Add instead of rename (below) |
| New required input field | **Breaking** | Make it optional with a default, or add a new action |
| Remove or retype an input or output field | **Breaking** | Keep the old field and deprecate it in the description |
| Change a field's meaning or units | **Breaking** | New field name |
| Change an enum's values | **Breaking** | Add values; never rename or remove one |
| Make a non-destructive action destructive, or change what a no-op does | **Breaking** in behavior | New action |
| Change an error code string | **Breaking** | Never. Codes are stable identifiers |

## Add instead of rename

1. Add the new field (or a new action with the new name) next to the old one.
2. The handler accepts both and prefers the new one. For an action rename, the old class stays and its description starts with "Deprecated: use <new_name>.".
3. Ship. Remove the old field or action only when you know no agent uses it; that removal is a breaking change and gets its own commit.

## Procedure

1. Classify the change with the table above. If it is breaking and cannot be made additive, say so explicitly in the commit message (`feat(projects)!: …` with a `BREAKING CHANGE:` footer).
2. Update the tests first (`skill:tdd`): new or changed cases in `tests/AppTemplate.Api.Tests/Features/<Aggregate>/<ActionClass>Tests.cs`, one `[Covers]` per new error code, and remove `[Covers]` for removed codes. `CoverageTests` fails on a missing code but not on a leftover one, so check by hand.
3. Change the input record, validator, handler, domain method and DTO (`skill:add-action`). Update the `[Action]` description when behavior, defaults or warnings change. Agents read it as the tool's documentation.
4. A persisted shape change needs a migration (`skill:ef-core`, "Data-preserving changes").
5. Regenerate the contract and the client:

   ```bash
   scripts/update-openapi.sh
   cd src/web && npm run gen:api
   ```

6. Read `git diff src/web/openapi.json` and check that every difference is intended. Then fix the TypeScript errors that `npm run typecheck` reports in `src/web/src/pages/`; they show every UI call site of the changed contract.
7. Search for prose that names the old contract and update it: other actions' error `Suggestion` texts that name the tool (`git grep -n "<old_name>"`), `src/AppTemplate.Api/Mcp/McpInstructions.cs`, `AGENTS.md` and the skills in `.claude/skills/`.
8. `scripts/verify.sh` must print `VERIFY OK`.
9. With the AppHost running (`dotnet run --project src/AppTemplate.AppHost`), list the tools through the `app` MCP server and read the changed tool's description and input schema as an agent would see them.

`OpenApiSnapshotTests` fails until you run `scripts/update-openapi.sh`, and `scripts/verify.sh` fails while `src/web/src/api/schema.d.ts` is out of date. Neither tells you whether the change breaks agents. That judgment is this skill.
