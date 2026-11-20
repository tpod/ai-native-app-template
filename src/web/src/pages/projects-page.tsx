import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { runQuery, type QueryResult } from '@/api/actions'
import { DataTable, type DataTableColumn } from '@/components/data-table'
import { HistoryButton } from '@/components/history-button'
import { InputActionForm } from '@/components/input-action-form'
import { ProblemAlert } from '@/components/problem-alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { useState } from 'react'

type Status = 'active' | 'archived' | null
type Summary = QueryResult<'list_projects'>[number]

const columns: DataTableColumn<Summary>[] = [
  {
    accessorKey: 'name',
    header: 'Name',
    cell: ({ row }) => (
      <Link className="font-medium underline-offset-4 hover:underline" to={`/projects/${row.original.id}`}>
        {row.original.name}
      </Link>
    ),
  },
  {
    accessorKey: 'status',
    header: 'Status',
    cell: ({ row }) => <Badge variant={row.original.status === 'active' ? 'default' : 'secondary'}>{row.original.status}</Badge>,
  },
  { accessorKey: 'taskCount', header: 'Tasks' },
  { accessorKey: 'openTaskCount', header: 'Open' },
  {
    id: 'history',
    header: '',
    enableSorting: false,
    cell: ({ row }) => <HistoryButton aggregateType="Project" aggregateId={row.original.id} label={row.original.name} />,
  },
]

const filters: { label: string; value: Status }[] = [
  { label: 'All', value: null },
  { label: 'Active', value: 'active' },
  { label: 'Archived', value: 'archived' },
]

export default function ProjectsPage() {
  const [status, setStatus] = useState<Status>(null)
  const { data, isPending, error } = useQuery({
    queryKey: ['projects', status],
    queryFn: () => runQuery('list_projects', { status }),
  })

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <h1 className="text-2xl font-semibold">Projects</h1>
        <InputActionForm
          action="create_project"
          label="New project"
          placeholder="Project name"
          submitLabel="New project"
          title="Create project"
          toInput={(name) => ({ name })}
        />
      </div>
      <div className="flex gap-2">
        {filters.map((f) => (
          <Button key={f.label} size="sm" variant={f.value === status ? 'default' : 'outline'} onClick={() => setStatus(f.value)}>
            {f.label}
          </Button>
        ))}
      </div>
      {error ? (
        <p className="text-destructive">{error.message}</p>
      ) : isPending ? (
        <p className="text-muted-foreground">Loading…</p>
      ) : 'problem' in data ? (
        <ProblemAlert problem={data.problem} />
      ) : (
        <DataTable columns={columns} data={data.data} emptyText="No projects yet." />
      )}
    </div>
  )
}
