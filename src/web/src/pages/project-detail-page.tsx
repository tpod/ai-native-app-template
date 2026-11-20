import { useMemo } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { runQuery, type QueryResult } from '@/api/actions'
import { DataTable, type DataTableColumn } from '@/components/data-table'
import { InputActionForm } from '@/components/input-action-form'
import { PreviewConfirmDialog } from '@/components/preview-confirm-dialog'
import { ProblemAlert } from '@/components/problem-alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'

type Task = QueryResult<'get_project'>['tasks'][number]

export default function ProjectDetailPage() {
  const { id = '' } = useParams()
  const { data, isPending, error } = useQuery({
    queryKey: ['project', id],
    queryFn: () => runQuery('get_project', { projectId: id }),
  })

  const columns = useMemo<DataTableColumn<Task>[]>(
    () => [
      { accessorKey: 'title', header: 'Task' },
      {
        accessorKey: 'done',
        header: 'Done',
        cell: ({ row }) => (row.original.done ? <Badge variant="secondary">done</Badge> : <Badge variant="outline">open</Badge>),
      },
      {
        id: 'actions',
        header: '',
        enableSorting: false,
        cell: ({ row }) =>
          row.original.done ? null : (
            <PreviewConfirmDialog
              action="complete_task"
              input={{ projectId: id, taskId: row.original.id }}
              title="Complete task"
              trigger={<Button size="sm" variant="outline">Complete</Button>}
            />
          ),
      },
    ],
    [id],
  )

  if (error) return <p className="text-destructive">{error.message}</p>
  if (isPending) return <p className="text-muted-foreground">Loading…</p>
  if ('problem' in data) {
    return (
      <div className="space-y-4">
        <ProblemAlert problem={data.problem} />
        <Link className="underline" to="/">Back to projects</Link>
      </div>
    )
  }
  const project = data.data

  return (
    <div className="space-y-6">
      <Link className="text-sm text-muted-foreground underline-offset-4 hover:underline" to="/">
        ← Projects
      </Link>
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div className="flex items-center gap-3">
          <h1 className="text-2xl font-semibold">{project.name}</h1>
          <Badge variant={project.status === 'active' ? 'default' : 'secondary'}>{project.status}</Badge>
        </div>
        {project.status === 'active' && (
          <PreviewConfirmDialog
            action="archive_project"
            input={{ projectId: id }}
            title="Archive project"
            destructive
            trigger={<Button variant="destructive">Archive</Button>}
          />
        )}
      </div>
      <InputActionForm
        key={project.name}
        action="rename_project"
        label="Name"
        initialValue={project.name}
        submitLabel="Rename"
        title="Rename project"
        toInput={(name) => ({ projectId: id, name })}
      />
      <InputActionForm
        action="add_task"
        label="New task"
        placeholder="Task title"
        submitLabel="Add task"
        title="Add task"
        toInput={(title) => ({ projectId: id, title })}
      />
      <DataTable columns={columns} data={project.tasks} emptyText="No tasks yet." />
    </div>
  )
}
