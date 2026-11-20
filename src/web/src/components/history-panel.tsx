import { useState } from 'react'
import { useInfiniteQuery } from '@tanstack/react-query'
import { Copy } from 'lucide-react'
import { runQuery, type QueryResult } from '@/api/actions'
import { ProblemAlert } from '@/components/problem-alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'

type Entry = QueryResult<'get_history'>[number]

export type HistoryFilter = { key: string; value: string }

type Props = {
  aggregateType: string
  aggregateId: string
  filter?: HistoryFilter
  /** Fetch only while the panel is open. */
  enabled: boolean
}

/** The page size asked of get_history. Pages run newest first; "Load more" fetches older entries. */
const PAGE_SIZE = 50

/** `project.task_added` → `Project task added`. */
function eventLabel(eventType: string) {
  const words = eventType.replace('.', ' ').replaceAll('_', ' ')
  return words.charAt(0).toUpperCase() + words.slice(1)
}

function relativeTime(iso: string) {
  const seconds = Math.round((new Date(iso).getTime() - Date.now()) / 1000)
  const units: [Intl.RelativeTimeFormatUnit, number][] = [
    ['day', 86400],
    ['hour', 3600],
    ['minute', 60],
  ]
  const format = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' })
  for (const [unit, size] of units) {
    if (Math.abs(seconds) >= size) return format.format(Math.round(seconds / size), unit)
  }
  return format.format(seconds, 'second')
}

function payloadEntries(payload: Entry['payload']): [string, string][] {
  if (typeof payload !== 'object' || payload === null) return []
  return Object.entries(payload as Record<string, unknown>).map(([k, v]) => [k, typeof v === 'string' ? v : JSON.stringify(v)])
}

function matches(entry: Entry, filter: HistoryFilter) {
  const value = (entry.payload as Record<string, unknown> | null)?.[filter.key]
  return value !== undefined && String(value).toLowerCase() === filter.value.toLowerCase()
}

/** The business history of one aggregate, newest first, from get_history. */
export function HistoryPanel({ aggregateType, aggregateId, filter, enabled }: Props) {
  const [showAll, setShowAll] = useState(false)
  const query = useInfiniteQuery({
    queryKey: ['history', aggregateType, aggregateId],
    queryFn: ({ pageParam }) =>
      runQuery('get_history', { aggregateType, aggregateId, newestFirst: true, beforeId: pageParam, limit: PAGE_SIZE }),
    // int64 ids arrive typed as string | number from the OpenAPI schema; they are passed back unchanged.
    initialPageParam: undefined as Entry['id'] | undefined,
    getNextPageParam: (last) => ('data' in last && last.data.length === PAGE_SIZE ? last.data[last.data.length - 1].id : undefined),
    enabled,
  })

  if (query.error) return <p className="text-destructive">{query.error.message}</p>
  if (query.isPending) return <p className="text-muted-foreground">Loading…</p>

  const problem = query.data.pages.find((p) => 'problem' in p)
  if (problem && 'problem' in problem) return <ProblemAlert problem={problem.problem} />

  const all = query.data.pages.flatMap((p) => ('data' in p ? p.data : []))
  const filtered = filter && !showAll
  const entries = filtered ? all.filter((e) => matches(e, filter)) : all

  return (
    <div className="space-y-4">
      {filter && !showAll && (
        <p className="text-sm text-muted-foreground">
          Showing entries for this task —{' '}
          <Button variant="link" className="h-auto p-0" onClick={() => setShowAll(true)}>
            show all
          </Button>
        </p>
      )}
      {entries.length === 0 && !query.hasNextPage && <p className="text-muted-foreground">No history recorded yet.</p>}
      <ol className="space-y-3">
        {entries.map((entry) => (
          <li key={entry.id} className="space-y-2 rounded-lg border p-3">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <span className="font-medium">{eventLabel(entry.eventType)}</span>
              <Tooltip>
                <TooltipTrigger asChild>
                  <time className="text-xs text-muted-foreground" dateTime={entry.occurredAt}>
                    {relativeTime(entry.occurredAt)}
                  </time>
                </TooltipTrigger>
                <TooltipContent>{new Date(entry.occurredAt).toLocaleString()}</TooltipContent>
              </Tooltip>
            </div>
            <div className="flex flex-wrap gap-1">
              <Badge variant="secondary">{entry.action}</Badge>
              <Badge variant="outline">{entry.client ? `${entry.channel} · ${entry.client}` : entry.channel}</Badge>
            </div>
            {entry.reason && (
              <p className="text-sm">
                <span className="text-muted-foreground">Reason given: </span>“{entry.reason}”
              </p>
            )}
            <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 text-xs">
              {payloadEntries(entry.payload).map(([key, value]) => (
                <div key={key} className="contents">
                  <dt className="text-muted-foreground">{key}</dt>
                  <dd className="font-mono break-all">{value}</dd>
                </div>
              ))}
            </dl>
            {entry.traceId && (
              <div className="flex items-center gap-1 text-xs text-muted-foreground">
                <span className="font-mono">trace {entry.traceId}</span>
                <Button
                  variant="ghost"
                  size="icon-xs"
                  aria-label="Copy trace id"
                  onClick={() => void navigator.clipboard.writeText(entry.traceId ?? '')}
                >
                  <Copy />
                </Button>
              </div>
            )}
          </li>
        ))}
      </ol>
      {query.hasNextPage && (
        <Button variant="outline" onClick={() => void query.fetchNextPage()} disabled={query.isFetchingNextPage}>
          Load more
        </Button>
      )}
    </div>
  )
}
