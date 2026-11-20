---
name: remove-example
description: Use when deleting the Projects/Tasks example feature from a fresh clone of the template, so only the framework remains and scripts/verify.sh still prints VERIFY OK.
---

# Remove the Projects example

Deletes the example aggregate (Projects with Tasks) from domain, Api, data, migrations, tests and web, and leaves the framework green. Run it before building your own features, on a clean working tree:

```bash
git status --short          # must print nothing
dotnet tool restore         # installs dotnet-ef from .config/dotnet-tools.json
```

Run every command from the repo root unless the step says otherwise. Follow the steps in order; step 5 needs steps 2 and 3, because dotnet-ef builds the Api.

## What the example touches

| Area | Path | Action |
|---|---|---|
| Domain | `src/AppTemplate.Domain/Projects/` | delete |
| Api features | `src/AppTemplate.Api/Features/Projects/` | delete |
| EF configuration (`ProjectConfiguration` and `TaskItemConfiguration`) | `src/AppTemplate.Api/Data/ProjectConfiguration.cs` | delete |
| DbContext (`Projects` DbSet, VO convention anchor) | `src/AppTemplate.Api/Data/AppDbContext.cs` | edit (step 2) |
| Migrations | `src/AppTemplate.Api/Data/Migrations/` | delete and re-add `InitialCreate` (step 5) |
| Feature tests | `tests/AppTemplate.Api.Tests/Features/Projects/` | delete |
| Domain tests | `tests/AppTemplate.Api.Tests/Domain/ProjectNameTests.cs`, `tests/AppTemplate.Api.Tests/Domain/ProjectTests.cs`, `tests/AppTemplate.Api.Tests/Domain/TaskTitleTests.cs` | delete |
| Persistence tests | `tests/AppTemplate.Api.Tests/Data/PersistenceTests.cs` | keep only the pending-model check (step 3) |
| Architecture guards that need an aggregate | `tests/AppTemplate.Architecture.Tests/ArchitectureTests.cs`, `tests/AppTemplate.Architecture.Tests/CoverageTests.cs` | no change; 6 guards skip themselves until an aggregate or action exists (step 4) |
| OpenAPI snapshot | `src/web/openapi.json` | regenerate (step 6) |
| Generated client | `src/web/src/api/schema.d.ts` | regenerate (step 7) |
| Pages | `src/web/src/pages/projects-page.tsx`, `src/web/src/pages/project-detail-page.tsx` | delete; add a placeholder page (step 8) |
| Routes | `src/web/src/main.tsx` | edit (step 8) |
| Preview dialog (generated `PreviewResponse` type) | `src/web/src/components/preview-confirm-dialog.tsx` | edit (step 8) |
| Docs | `AGENTS.md` | edit two lines (step 9) |

No change is needed in `src/web/src/App.tsx` (the layout has no Projects link; routes live in `main.tsx`), `src/AppTemplate.Api/Program.cs`, `src/AppTemplate.AppHost/AppHost.cs`, or the framework tests (`Actions/`, `Mcp/`, `OpenApi/`, `Telemetry/`, `Http/`), which use the Notes fixture. `tests/AppTemplate.Api.Tests/Http/SpaFallbackTests.cs` and `tests/AppTemplate.Api.Tests/Domain/AppErrorTests.cs` contain the strings `/projects/123` and `project_name.empty` as plain test data; leave them.

## 1. Delete the example files

```bash
git rm -r -q src/AppTemplate.Domain/Projects src/AppTemplate.Api/Features/Projects src/AppTemplate.Api/Data/ProjectConfiguration.cs
git rm -r -q tests/AppTemplate.Api.Tests/Features/Projects tests/AppTemplate.Api.Tests/Domain/ProjectNameTests.cs tests/AppTemplate.Api.Tests/Domain/ProjectTests.cs tests/AppTemplate.Api.Tests/Domain/TaskTitleTests.cs
git rm -r -q src/web/src/pages/projects-page.tsx src/web/src/pages/project-detail-page.tsx
git rm -r -q src/AppTemplate.Api/Data/Migrations
```

`git rm` of the two pages also removes the empty `src/web/src/pages` directory; step 8 recreates it.

## 2. Edit `src/AppTemplate.Api/Data/AppDbContext.cs`

Three changes:

1. Replace `using AppTemplate.Domain.Projects;` with `using AppTemplate.Domain.Common;`.
2. Delete the line `public DbSet<Project> Projects => Set<Project>();` and the blank line after it.
3. In `ConfigureConventions`, replace `typeof(Project).Assembly` with `typeof(IAggregateRoot).Assembly`. It must still point at the Domain assembly, so VOs you add later are mapped by convention.

## 3. Replace `tests/AppTemplate.Api.Tests/Data/PersistenceTests.cs`

Its other tests exercise `Project`. Keep only the check that the migrations match the model. Write the whole file as:

```csharp
using AppTemplate.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace AppTemplate.Api.Tests.Data;

public sealed class PersistenceTests
{
    [Fact]
    public async Task Model_AfterMigrations_HasNoPendingChanges()
    {
        using var db = new SqliteTestDatabase();
        await using var c = db.NewContext();
        c.Database.HasPendingModelChanges().ShouldBeFalse();
        await Task.CompletedTask;
    }
}
```

## 4. Nothing to do for the architecture guards

Six guards assert that the collection they check is non-empty, so an empty loop cannot pass vacuously. They skip themselves while there is nothing to check, and they re-enable automatically. Do not edit them. The conditions live in `tests/AppTemplate.Architecture.Tests/GuardConditions.cs` and are plain reflection scans:

| Guard | Runs when |
|---|---|
| `DomainEntities_HaveNoPublicSetters`, `ValueObjects_AreSealedWithoutPublicParameterizedConstructors`, `AggregateRoots_VersionIsConcurrencyToken` | the Domain assembly has a concrete subclass of `AggregateRoot<>` (`DomainHasAggregate`) |
| `Registry_ApiAssembly_BuildsWithoutViolations`, `EveryAction_HasMatchingTestsClass`, `EveryAction_HasCoversForHappyPathEachErrorAndStale` | the Api assembly has a type marked `[Action]` or `[Query]` (`ApiHasActions`) |

After this skill, all six report as skipped, with "No aggregate in the Domain yet; …" or "No [Action] or [Query] in the Api yet; …". The first aggregate or action you add turns them back on, and they must then pass.

## 5. Re-add the `InitialCreate` migration

```bash
dotnet build AppTemplate.slnx
dotnet tool run dotnet-ef migrations add InitialCreate -p src/AppTemplate.Api -o Data/Migrations
```

The build must report `0 Warning(s)` and `0 Error(s)`; it also proves steps 2 to 4 compile. Do not skip it: in a fresh clone the projects are not restored yet, and dotnet-ef then fails with `NETSDK1004: Assets file … project.assets.json not found`.

Expected from dotnet-ef: `Build succeeded.` then `Done.`, and three new files in `src/AppTemplate.Api/Data/Migrations/` (`<timestamp>_InitialCreate.cs`, its `.Designer.cs` and `AppDbContextModelSnapshot.cs`). `Up` and `Down` are empty, because the model has no entities yet. That is correct: the first real migration comes with your first aggregate.

## 6. Regenerate the OpenAPI snapshot

```bash
scripts/update-openapi.sh
```

Expected: the test run passes, and `src/web/openapi.json` now has `"paths": {}`.

## 7. Regenerate the TypeScript client

```bash
cd src/web && npm run gen:api
```

`src/web/src/api/schema.d.ts` now declares `export type paths = Record<string, never>;`. Run `npm ci` in `src/web` first if its node_modules folder is missing.

## 8. Fix the web app

`npm run typecheck` in `src/web` now fails in `src/web/src/main.tsx` (missing pages) and in `src/web/src/components/preview-confirm-dialog.tsx` (the schema has no `PreviewResponse` while there are no actions). Make these changes.

**8a. Create `home-page.tsx` in `src/web/src/pages/`** (recreate the directory first):

```tsx
export default function HomePage() {
  return (
    <div className="space-y-2">
      <h1 className="text-2xl font-semibold">Home</h1>
      <p className="text-muted-foreground">No features yet. Add an aggregate, its actions and a page for them.</p>
    </div>
  )
}
```

**8b. Edit `src/web/src/main.tsx`.** Replace the two imports

```tsx
import ProjectDetailPage from '@/pages/project-detail-page'
import ProjectsPage from '@/pages/projects-page'
```

with `import HomePage from '@/pages/home-page'`. Then replace the two routes

```tsx
            <Route index element={<ProjectsPage />} />
            <Route path="projects/:id" element={<ProjectDetailPage />} />
```

with `<Route index element={<HomePage />} />`, keeping the 12-space indentation.

**8c. Edit `src/web/src/components/preview-confirm-dialog.tsx`.**

1. Delete the line `import type { components } from '@/api/schema'`.
2. Replace the line `type Preview = components['schemas']['PreviewResponse']` with:

   ```tsx
   // Mirrors PreviewResponse in src/AppTemplate.Api/Actions/Responses.cs. Declared here because the generated
   // schema has no PreviewResponse while the app has no actions.
   type Preview = {
     action: string
     summary: string
     changes: { entity: string; id: string; op: string; field: string; before: unknown; after: unknown }[]
     sideEffects: { event: string; payload: unknown }[]
     warnings: { code: string; message: string }[]
     previewToken: string
     expiresAt: string
   }
   ```

The dialog's query invalidation is already generic (every query except previews), so it needs no change.

Check the web app:

```bash
cd src/web && npm run typecheck && npm run lint
```

Both must exit 0 with no output beyond the script banners.

## 9. Update `AGENTS.md`

1. In the architecture map, delete the line `    Projects/                   # the example aggregate (remove with skill:remove-example)`.
2. Under "Errors", replace "See `src/AppTemplate.Domain/Projects/ProjectErrors.cs`." with "See `src/AppTemplate.Domain/Common/ValidationFailed.cs`.".
3. Under "Swap to Postgres", delete the last sentence, which starts "The `NOCASE` collation on `ProjectName`".

The other Projects names in `AGENTS.md` and in `.claude/rules/` (`rename_project`, `ProjectName.Create`, `project.name_taken`) are illustrations of the patterns and stay valid as such. `git grep -n -i project -- AGENTS.md .claude/rules` lists them; rewrite them for your own aggregate when you have one.

The skills in `.claude/skills/` keep the Projects files as their worked examples. `SkillLintTests` stops checking those paths once `src/AppTemplate.Domain/Projects` is gone, and `skill:add-action` explains how to read them from Git history.

## 10. Verify

```bash
scripts/verify.sh
```

It must end with `VERIFY OK`. The `dotnet test` step reports 6 skipped tests (the self-skipping guards from step 4) and no failures.

Commit, for example: `git add -A && git commit -m "refactor!: remove the Projects example"`.

Next: `skill:add-aggregate` for your first aggregate. The six guards re-enable on their own as soon as it exists.
