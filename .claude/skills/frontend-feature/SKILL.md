---
name: frontend-feature
description: Use when adding or changing a page in src/web for an action or query, using the generated client, TanStack Query, DataTable, PreviewConfirmDialog and shadcn components.
---

# Frontend feature

The web app is React + Vite + TypeScript with Tailwind v4, shadcn/ui (radix base, lucide icons), TanStack Query, TanStack Table v9 and react-router. It talks to the Api only through the client generated from `src/web/openapi.json`. Run every npm and shadcn command from `src/web`.

Worked examples: `src/web/src/pages/projects-page.tsx` (list, filter, create) and `src/web/src/pages/project-detail-page.tsx` (detail, child table, row actions, destructive action).

## 1. Make sure the client knows the action

The action must already exist (`skill:add-action`). Regenerate after any Api contract change:

```bash
scripts/update-openapi.sh
cd src/web && npm run gen:api
```

Never edit `src/web/src/api/schema.d.ts` by hand. `scripts/verify.sh` fails when it drifts from `src/web/openapi.json`.

## 2. Typed calls

`src/web/src/api/actions.ts` derives names and types from the schema:

| Need | Use |
|---|---|
| Run a query | `runQuery('list_projects', { status })` |
| Row or result type | `QueryResult<'list_projects'>[number]`, `QueryResult<'get_project'>['tasks'][number]` |
| Input type of an action | `ActionInput<'rename_project'>` |
| Preview / execute by hand | `previewAction(name, input)`, `executeAction(name, input, previewToken)`; prefer the dialog below |

Every call returns `{ data } | { problem }`, where `problem` is the ProblemDetails body (`code`, `suggestion`, `traceId`, …). It never throws for an error response. Check `'problem' in result` and render `<ProblemAlert problem={result.problem} />` (`src/web/src/components/problem-alert.tsx`).

## 3. Reads with TanStack Query

```tsx
const { data, isPending, error } = useQuery({
  queryKey: ['projects', status],
  queryFn: () => runQuery('list_projects', { status }),
})
```

Key queries by aggregate: `['<aggregates>', filters]` for lists and `['<aggregate>', id]` for detail. After a successful execute, `PreviewConfirmDialog` (`src/web/src/components/preview-confirm-dialog.tsx`) invalidates every query except previews, so your page refetches without any change to the dialog.

Render the states in this order: `error` (network), `isPending`, `'problem' in data`, then the data.

## 4. Lists use `<DataTable>`

`src/web/src/components/data-table.tsx`, with click-to-sort headers.

```tsx
type Summary = QueryResult<'list_projects'>[number]

const columns: DataTableColumn<Summary>[] = [
  { accessorKey: 'name', header: 'Name' },
  { accessorKey: 'status', header: 'Status', cell: ({ row }) => <Badge>{row.original.status}</Badge> },
]

<DataTable columns={columns} data={data.data} emptyText="No projects yet." />
```

Keep `columns` stable: define it at module scope, or in `useMemo` when it needs props (row action buttons). A new array on every render rebuilds the table. Set `enableSorting: false` on action columns.

### Every entity gets a history button

Every page that shows an aggregate or a child entity puts a `<HistoryButton>` (`src/web/src/components/history-button.tsx`) on it: beside a detail page's title, and in a column of each list row. It opens the entity's business history from `get_history` in a side panel.

```tsx
<HistoryButton aggregateType="Project" aggregateId={project.id} label={project.name} />
{/* A child entity has no history of its own: pass its root, plus a filter on its id key in the event payloads. */}
<HistoryButton aggregateType="Project" aggregateId={projectId} filter={{ key: 'taskId', value: task.id }} label={task.title} />
```

The panel's query key is `['history', aggregateType, aggregateId]` and it fetches only while open, so the invalidate-after-execute rule keeps it fresh.

## 5. Every mutation goes through `<PreviewConfirmDialog>`

```tsx
<PreviewConfirmDialog
  action="archive_project"
  input={{ projectId: id }}
  title="Archive project"
  destructive
  trigger={<Button variant="destructive">Archive</Button>}
/>
```

It previews when it opens, shows `summary`, `changes`, `sideEffects` and `warnings`, executes with the preview token on Confirm, and shows a Re-preview button on `preview.stale` or `preview.expired`. Pass `destructive` for actions with `Destructive = true`. Never call `executeAction` without a preview in the UI.

For an action whose input is one text value, use `<InputActionForm>` (`src/web/src/components/input-action-form.tsx`). It wraps the dialog:

```tsx
<InputActionForm
  action="rename_project"
  label="Name"
  initialValue={project.name}
  submitLabel="Rename"
  title="Rename project"
  toInput={(name) => ({ projectId: id, name })}
/>
```

## 6. Page and route

1. Create `src/web/src/pages/<aggregate>-page.tsx` (kebab-case) with a default export.
2. Add the route in `src/web/src/main.tsx`, inside the `<Route element={<App />}>` layout route: `<Route path="invoices/:id" element={<InvoiceDetailPage />} />`.
3. Add a link to the layout header in `src/web/src/App.tsx` if the page is top-level.

## 7. Components and styling

- Check what is installed in `src/web/src/components/ui/` first. Add a missing component with `npx shadcn@latest add <component>`, run from `src/web`. Never edit files in `src/web/src/components/ui/`; compose on top in `src/web/src/components/`.
- Follow `skill:shadcn` for component choice and composition (Field/FieldGroup for forms, `Empty`, `Alert`, `Badge`, `size-*`, `data-icon` on icons in buttons, `gap-*` rather than `space-y-*` in new markup). Its context command `npx shadcn@latest info --json` only works from `src/web`, where `components.json` is.
- The `shadcn` MCP server starts at the repo root, where there is no `components.json`. Pass `registries: ["@shadcn"]` to its search and list tools.
- Tailwind semantic tokens only (`bg-background`, `text-muted-foreground`, `text-destructive`). No custom CSS and no raw colors.
- Use `toast` from `sonner` for success messages; `PreviewConfirmDialog` already toasts on execute.

## 8. Verify

```bash
cd src/web && npm run typecheck && npm run lint && npm run build
```

Then `scripts/verify.sh` must print `VERIFY OK`. To click through, run `dotnet run --project src/AppTemplate.AppHost` and open the `web` resource's URL from the Aspire dashboard (`http://localhost:15165`). Vite proxies `/api` and `/mcp` to the Api.
