# Template v1: deferred follow-ups

These are Minor findings from the per-task reviews and the final whole-branch review of `feat/template-v1`. The final reviewer triaged each one as "keep deferred". None blocks merge. Findings that were fixed before merge are not listed.

## Not verified in v1

- **UI:** no browser was available, so the UI was never checked visually. The same flows were exercised over HTTP through the Vite proxy.
- **`skill:swap-to-postgres`:** never dry-run, because there is no Docker and the Npgsql packages aren't cached.
- **`NoAssembly_ReferencesEfCoreInMemory`:** never shown to fail on a real violation, because the InMemory package isn't in the offline cache.

## Framework: actions, preview, errors

- `ActionRegistry` name regex: `$` accepts a trailing newline. Use `\z`.
- `PreviewLifetimeSeconds <= 0` is not rejected.
- `AddActions`:
  - The configure delegate runs twice.
  - `IsHandler` duplicates the registry checks.
  - There are two handler-resolution paths.
  - A second call adds a duplicate enum converter.
- `previewToken` is matched case-sensitively, but input binds case-insensitively.
- A unique-index race between creates with different ids returns 500. It could map to stale `concurrent_write`.
- Token handling:
  - Base64Url decoding is lenient, so one payload can have several accepted token strings.
  - The payload deserializer doesn't enforce required params.
  - A local-kind `DateTime` makes the hash timezone-dependent.
- The three-key change sort is duplicated in `ChangeSetReader` and `ChangeSetHasher`.
- Framework status pages map non-404 responses, including bare 5xx and the `/mcp` 405, to `request.malformed`, with a body-oriented suggestion. Add `request.method_not_allowed`, and map ≥500 to `internal.unexpected`.
- 4xx responses that come from body parsing or the fallback are not logged.
- `MissingRequiredField` checks top-level input properties only. A missing nested field reports path `$`.
- `ErrorKind.Forbidden` was inserted before `Unexpected`, which changes the numeric value. Nothing persists it today.
- `RespectNullableAnnotations` also applies to output. A null in a non-nullable DTO property now returns 500.
- `ActionLog.ActionUnexpected` is dead code. Client cancellation is tagged `internal.unexpected` with Error status.
- Derived validation field codes pass digits through (`address2.not_empty`), which breaks the code regex.

## MCP and OpenAPI

- `ListTools`, and the `CallTool` lines before its `try`, can fail without ProblemDetails or a log line.
- The MCP traceId fallback differs from `TraceIdMiddleware.Resolve`.
- The server instructions paraphrase the execute confirmation sentence. Share one constant.
- MCP tests check one sample per output kind, not every tool. The scalar `{value}` wrapping branch is untested.
- OpenAPI:
  - The previewToken transformer and execute detection string-match routes, in two places.
  - `UPDATE_OPENAPI=1` in CI would mask drift.
  - Query operations aren't covered by the problem-response test.
- `ExecuteResponse.result` is untyped (`{}`) in OpenAPI, so the TS client can't type execute results.
- Ints export as `number | string` because of the web JSON defaults' `NumberHandling`.
- There is no CORS policy for the standalone static bundle (`VITE_API_BASE_URL`). Document it, or add an opt-in policy.
- The Origin allow-list accepts `[::1]`, but the Development `AllowedHosts` does not.

## Tests

- `ShouldFailWith` doesn't assert `type`, `title`, `detail` or `instance`.
- There are no tests for:
  - 415, or a POST to an unknown `/api` route through the real `Program`;
  - stale `domain_error` with a `Cause`, or `concurrent_write`;
  - explicit or null `WithErrorCode`;
  - the Development signing-key fallback;
  - complex properties in the change set;
  - invoker arity 3+, record-struct input, explicit-interface `Summarize`, and exception propagation.
- Some registry violation assertions match on type-name text, so they pass whatever the message says.
- `Read_VersionIncremented_IsExcluded` is misnamed.
- `/health` is enabled in `Testing` by editing ServiceDefaults. A factory toggle would be cleaner.
- `ApiFactory` disposes its database only in `DisposeAsync`.
- `SharedCodes` should be pinned to exactly `RequestNotFound` and `McpToolNotFound`.
- No test checks that `Version` is the *only* concurrency token.
- `ValueObjects_AreSealedWithoutPublicParameterizedConstructors` skips record-struct VOs and doesn't check immutability.
- `CoverageTests` detects validators by reflection, while the runtime resolves them from DI. The two can drift.
- `ApiHasActions` doesn't exclude abstract types. That fails loudly if it's ever hit.
- After `remove-example`:
  - no framework test covers a child-entity FK in the change set;
  - `add-aggregate` doesn't rebuild the PersistenceTests round-trip and concurrency checks.

## Projects example

- `RenameProject` repeats the same-name rule in the action. The domain should signal "unchanged".
- Summaries use `input.Trim()` instead of the value object's value.
- `ProjectDto.Tasks` order is undefined. Order by `Id`, which is a v7 Guid.
- Name uniqueness relies on `NOCASE` with no unique index, so concurrent creates can race.

## Web

- Casts:
  - `RawPost` erases openapi-fetch path typing.
  - The preview-body cast hides drift.
- Stale preview content flashes on reopen. Confirm is disabled until the preview is refetched.
- After `remove-example`, the hand-copied `Preview` type in `preview-confirm-dialog.tsx` can drift from `Responses.cs`.
- `BuildWeb` has no `SkipWebBuild` condition.
- `tsconfig` `paths`/`baseUrl` are duplicated.
- One ESLint ignore has no effect.
- `scripts/verify.sh` runs `npm ci` only when `node_modules` is missing, so a stale install passes.

## Scripts, config, docs

- `rename.sh`:
  - `grep -i` doesn't match the exact-case `sed`.
  - The npm package keeps the name `app-template-web`.
  - Untracked dirs are found on disk.
  - It should regenerate the AppHost `UserSecretsId`. Today every clone on one machine shares the persisted signing key.
- `launchSettings.json` was rewritten whole (line-ending churn).
- AGENTS.md:
  - The async-suffix and one-type-per-file override wording is narrower than repo practice.
  - `architecture.md:65` still has integration-events advice.
  - The Context7 key is stored in plain text in `~/.claude.json`.
- Skill lint:
  - Its exemption uses raw string prefixes duplicated from the `remove-example` table.
  - Bare root files and symbols aren't linted.
- After removal, skills other than `add-action` point at deleted example files with no git-history pointer.
- `remove-example` table wording: it says "two lines" but makes three edits. The placeholder page uses `space-y-2`.
- The vendored shadcn skill's `allowed-tools` pre-approves `npx shadcn@latest *`, including `apply`, `init --force` and `add --overwrite`. Consider `permissions.ask` entries.
- The AppHost doesn't print the web resource URL on the console; it only shows in the dashboard.
