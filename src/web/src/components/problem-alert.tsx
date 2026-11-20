import type { ReactNode } from 'react'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import type { Problem } from '@/api/actions'

type Props = { problem: Problem; children?: ReactNode }

/** Renders a ProblemDetails error: title, detail, suggestion and a small trace id. `children` holds optional actions. */
export function ProblemAlert({ problem, children }: Props) {
  return (
    <Alert variant="destructive">
      <AlertTitle>
        {problem.title} <span className="font-mono text-xs font-normal">({problem.code})</span>
      </AlertTitle>
      <AlertDescription>
        <p>{problem.detail}</p>
        {problem.suggestion && <p className="text-muted-foreground">{problem.suggestion}</p>}
        {problem.traceId && <p className="font-mono text-xs text-muted-foreground">trace {problem.traceId}</p>}
        {children}
      </AlertDescription>
    </Alert>
  )
}
