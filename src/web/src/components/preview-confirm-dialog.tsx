import { useState, type ReactNode } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { toast } from 'sonner'
import {
  executeAction,
  previewAction,
  type ActionInput,
  type ActionName,
  type PreviewInput,
  type PreviewName,
  type Problem,
} from '@/api/actions'
import type { components } from '@/api/schema'
import { ProblemAlert } from '@/components/problem-alert'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from '@/components/ui/dialog'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'

type Props<N extends ActionName & PreviewName> = {
  action: N
  input: ActionInput<N>
  /** Rendered inside a DialogTrigger; the dialog owns its open state, so callers just pass a button. */
  trigger: ReactNode
  title: string
  destructive?: boolean
  onExecuted?: (result: unknown) => void
}

type Preview = components['schemas']['PreviewResponse']

const show = (v: unknown) => (v === null || v === undefined ? '—' : typeof v === 'string' ? v : JSON.stringify(v))

/**
 * Every UI mutation goes through here: preview on open, show what will change, execute with the
 * preview token on confirm. Uncontrolled via `trigger` (the cleanest fit: each call site is a button).
 */
export function PreviewConfirmDialog<N extends ActionName & PreviewName>({
  action,
  input,
  trigger,
  title,
  destructive = false,
  onExecuted,
}: Props<N>) {
  const [open, setOpen] = useState(false)
  const queryClient = useQueryClient()

  // Preview tokens are single-use, so never cache: refetch on every open and on "Re-preview".
  const preview = useQuery({
    queryKey: ['preview', action, input],
    queryFn: () => previewAction(action, input as PreviewInput<N>),
    enabled: open,
    gcTime: 0,
    staleTime: 0,
    retry: false,
    refetchOnWindowFocus: false,
  })

  const execute = useMutation({
    mutationFn: (token: string) => executeAction(action, input, token),
    onSuccess: async (result) => {
      if ('problem' in result) return
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['projects'] }),
        queryClient.invalidateQueries({ queryKey: ['project'] }),
      ])
      toast.success(`${title} done`)
      setOpen(false)
      onExecuted?.(result.data)
    },
  })

  const handleOpenChange = (next: boolean) => {
    // Closing mid-execute would let the late success close a later dialog.
    if (!next && execute.isPending) return
    setOpen(next)
    if (next) execute.reset()
  }

  const previewResult = preview.data
  const previewData = previewResult && 'data' in previewResult ? (previewResult.data as Preview) : undefined
  const problem: Problem | undefined =
    (previewResult && 'problem' in previewResult ? previewResult.problem : undefined) ??
    (execute.data && 'problem' in execute.data ? execute.data.problem : undefined) ??
    (preview.error ? networkProblem(preview.error) : undefined) ??
    (execute.error ? networkProblem(execute.error) : undefined)
  const loading = preview.isFetching
  const repreview = () => {
    execute.reset()
    void preview.refetch()
  }

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogTrigger asChild>{trigger}</DialogTrigger>
      <DialogContent className="sm:max-w-xl">
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          <DialogDescription>{loading ? 'Loading preview…' : (previewData?.summary ?? 'Preview')}</DialogDescription>
        </DialogHeader>

        {problem && (
          <ProblemAlert problem={problem}>
            {(problem.code === 'preview.stale' || problem.code === 'preview.expired') && (
              <Button variant="outline" size="sm" className="mt-2" onClick={repreview}>
                Re-preview
              </Button>
            )}
          </ProblemAlert>
        )}

        {!problem && previewData && (
          <div className="space-y-3 text-sm">
            {previewData.changes.length > 0 && (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Entity</TableHead>
                    <TableHead>Field</TableHead>
                    <TableHead>Before → After</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {previewData.changes.map((c, i) => (
                    <TableRow key={`${c.entity}-${c.id}-${c.field}-${i}`}>
                      <TableCell>
                        {c.entity} <span className="text-muted-foreground">({c.op})</span>
                      </TableCell>
                      <TableCell>{c.field}</TableCell>
                      <TableCell>
                        {show(c.before)} → {show(c.after)}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}
            {previewData.sideEffects.length > 0 && (
              <div>
                <p className="font-medium">Side effects</p>
                <ul className="list-disc pl-5 text-muted-foreground">
                  {previewData.sideEffects.map((s, i) => (
                    <li key={`${s.event}-${i}`}>
                      {s.event} <span className="font-mono text-xs">{show(s.payload)}</span>
                    </li>
                  ))}
                </ul>
              </div>
            )}
            {previewData.warnings.length > 0 && (
              <div>
                <p className="font-medium text-destructive">Warnings</p>
                <ul className="list-disc pl-5">
                  {previewData.warnings.map((w) => (
                    <li key={w.code}>{w.message}</li>
                  ))}
                </ul>
              </div>
            )}
          </div>
        )}

        <DialogFooter>
          <Button variant="outline" disabled={execute.isPending} onClick={() => setOpen(false)}>
            Cancel
          </Button>
          {!problem && (
            <Button
              variant={destructive ? 'destructive' : 'default'}
              disabled={loading || !previewData || execute.isPending}
              onClick={() => previewData && execute.mutate(previewData.previewToken)}
            >
              {execute.isPending ? 'Working…' : 'Confirm'}
            </Button>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function networkProblem(error: unknown): Problem {
  return {
    type: 'about:blank',
    title: 'Request failed',
    status: 0,
    detail: error instanceof Error ? error.message : 'The request could not be completed.',
    code: 'network.failed',
    suggestion: 'Check your connection and try again.',
    traceId: '',
  }
}
