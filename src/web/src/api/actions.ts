import { api } from './client'
import type { paths } from './schema'

export type Problem = {
  type: string
  title: string
  status: number
  detail: string
  code: string
  details?: unknown
  suggestion: string
  traceId: string
}

export type Result<T> = { data: T } | { problem: Problem }

export function isProblem(x: unknown): x is Problem {
  return (
    typeof x === 'object' &&
    x !== null &&
    typeof (x as Problem).code === 'string' &&
    typeof (x as Problem).title === 'string' &&
    typeof (x as Problem).status === 'number'
  )
}

type PathsWithPrefix<Prefix extends string> = {
  [P in keyof paths]: P extends `${Prefix}${infer Name}` ? Name : never
}[keyof paths]

/** Names of actions that have a preview route (`/api/actions/{name}/preview`). */
export type PreviewName = PathsWithPrefix<'/api/actions/'> extends infer N
  ? N extends `${infer Name}/preview`
    ? Name
    : never
  : never
/** Names of actions that have an execute route (`/api/actions/{name}`). */
export type ActionName = Exclude<PathsWithPrefix<'/api/actions/'>, `${string}/${string}`>
export type QueryName = PathsWithPrefix<'/api/queries/'>

type JsonBody<Op> = Op extends { requestBody: { content: { 'application/json': infer B } } } ? B : never
type JsonOk<Op> = Op extends { responses: { 200: { content: { 'application/json': infer R } } } } ? R : never
type PostOp<P extends keyof paths> = paths[P] extends { post: infer Op } ? Op : never

export type PreviewInput<N extends PreviewName> = JsonBody<PostOp<`/api/actions/${N}/preview`>>
export type PreviewResult<N extends PreviewName> = JsonOk<PostOp<`/api/actions/${N}/preview`>>
export type ActionInput<N extends ActionName> = Omit<JsonBody<PostOp<`/api/actions/${N}`>>, 'previewToken'>
export type ActionResult<N extends ActionName> = JsonOk<PostOp<`/api/actions/${N}`>>
export type QueryInput<N extends QueryName> = JsonBody<PostOp<`/api/queries/${N}`>>
export type QueryResult<N extends QueryName> = JsonOk<PostOp<`/api/queries/${N}`>>

// openapi-fetch cannot follow a path built from a generic name, so the one untyped hop lives here;
// the exported signatures above are what callers see.
type RawPost = (
  path: string,
  init: { body: unknown },
) => Promise<{ data?: unknown; error?: unknown; response: Response }>

async function post<T>(path: string, body: unknown): Promise<Result<T>> {
  const { data, error, response } = await (api.POST as unknown as RawPost)(path, { body })
  if (response.ok) return { data: data as T }
  if (isProblem(error)) return { problem: error }
  return {
    problem: {
      type: 'about:blank',
      title: response.statusText || 'Unexpected response',
      status: response.status,
      detail: typeof error === 'string' ? error : 'The server returned a non-problem error response.',
      code: 'response.unexpected',
      suggestion: 'Retry; if it persists, check the server logs.',
      traceId: response.headers.get('X-Trace-Id') ?? '',
    },
  }
}

export function previewAction<N extends PreviewName>(name: N, input: PreviewInput<N>) {
  return post<PreviewResult<N>>(`/api/actions/${name}/preview`, input)
}

export function executeAction<N extends ActionName>(name: N, input: ActionInput<N>, previewToken: string) {
  return post<ActionResult<N>>(`/api/actions/${name}`, { ...input, previewToken })
}

export function runQuery<N extends QueryName>(name: N, input: QueryInput<N>) {
  return post<QueryResult<N>>(`/api/queries/${name}`, input)
}
