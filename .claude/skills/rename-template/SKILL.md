---
name: rename-template
description: Use when renaming a fresh clone of the template from AppTemplate to the real product name (solution, projects, namespaces, file and folder names).
---

# Rename the template

`scripts/rename.sh <NewName>` replaces the `AppTemplate` prefix everywhere Git tracks it: file contents (`AppTemplate` becomes `<NewName>`, `apptemplate` becomes the lowercase name), file names and folder names, using `git mv`. Untracked files are not touched.

## Before

1. Install the per-machine prerequisites if this machine does not have them yet (AGENTS.md → "Plugins and prerequisites"):
   - Aspire CLI, for the `aspire` MCP server: `curl -sSL https://aspire.dev/install.sh | bash`, or `dotnet tool install -g Aspire.Cli`.
   - Roslyn navigator, for the dotnet-claude-kit MCP server: `dotnet tool install -g CWM.RoslynNavigator`.
2. Pick the name. It must match `^[A-Z][A-Za-z0-9]+$` (PascalCase, no dots, dashes or spaces), for example `Acme` or `TaskFlow`. It becomes the solution name and the namespace root (`TaskFlow.Api`, `TaskFlow.Domain`).
3. Start from a clean, committed tree so the rename is one reviewable commit: `git status --short` must print nothing.
4. Stop any running AppHost (`dotnet run`), so no build output is locked.

## Run

```bash
scripts/rename.sh <NewName>
```

The script prints `Renamed to <NewName>. Next steps:` when it finishes. It exits 1 with a usage line if the name is invalid.

## Check the result

Run these from the repo root (substitute your name):

```bash
git grep -n -i "app""template" || echo "no leftovers"   # expect: no leftovers
ls <NewName>.slnx src/<NewName>.Api src/<NewName>.Domain src/<NewName>.AppHost
git status --short | head                          # renames (R) and modifications (M) only
```

The pattern is split (`"app""template"`) for the same reason `scripts/rename.sh` splits it: the script rewrites every tracked file, this one included, and would otherwise turn the check into a search for the new name.

- `CLAUDE.md` must still contain exactly `@AGENTS.md`.
- `src/web/package.json` keeps its npm name `app-template-web` (hyphenated, so the script does not match it). Rename it by hand if you want, then run `npm install` in `src/web` to update `src/web/package-lock.json`.
- The AppHost user-secrets id (`<UserSecretsId>` in `src/<NewName>.AppHost/<NewName>.AppHost.csproj`) is the same GUID in every clone of the template. Replace it with a new GUID (`uuidgen`), so this app does not share the generated preview signing key with other apps made from the template on the same machine.
- Old build output under `bin/` and `obj/` still has the old names. The next build recreates it. If the build reports duplicate or missing assemblies, run `dotnet clean <NewName>.slnx` and build again.

## Verify and commit

```bash
dotnet build <NewName>.slnx
scripts/verify.sh
```

`scripts/verify.sh` must print `VERIFY OK`. It also runs `SkillLintTests`, which confirms that every path the skills mention was renamed consistently. Then commit:

```bash
git add -A
git commit -m "chore: rename template to <NewName>"
```

After the rename, every command in `AGENTS.md` and the skills uses the new prefix, for example `dotnet run --project src/<NewName>.AppHost`. The script rewrote those files too.
