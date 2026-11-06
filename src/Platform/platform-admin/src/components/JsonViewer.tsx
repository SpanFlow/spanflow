import { useMemo, useState, type ReactNode } from 'react'

/**
 * A JSON payload viewer with a collapsible tree, a raw (pretty-printed) toggle, and find. Parsing the
 * text is also what fixes escaped output: a value stored as `"` or `é` shows as `"`/`é` once
 * parsed. A double-encoded payload (a JSON string that is itself JSON) is unwrapped. Very large or
 * non-JSON text falls back to a raw block so the tab never hangs.
 */

/** Above this we skip the interactive tree — parsing + rendering a huge tree janks the main thread. */
const TREE_MAX = 2 * 1024 * 1024 // 2 MB

type Parsed = { ok: true; value: unknown } | { ok: false }

function parseLoose(text: string): Parsed {
  try {
    let value = JSON.parse(text)
    // Unwrap a double-encoded payload: a JSON string whose content is itself JSON.
    if (typeof value === 'string') {
      const t = value.trim()
      if (t.startsWith('{') || t.startsWith('[')) {
        try {
          value = JSON.parse(value)
        } catch {
          /* keep the plain string */
        }
      }
    }
    return { ok: true, value }
  } catch {
    return { ok: false }
  }
}

const isContainer = (v: unknown): v is unknown[] | Record<string, unknown> =>
  v !== null && typeof v === 'object'

const entriesOf = (v: unknown[] | Record<string, unknown>): [string, unknown][] =>
  Array.isArray(v) ? v.map((x, i) => [String(i), x]) : Object.entries(v)

function ancestorsOf(path: string): string[] {
  const parts = path.split('/')
  const out: string[] = []
  for (let i = 1; i < parts.length; i++) out.push(parts.slice(0, i).join('/'))
  return out
}

/** Paths whose key or primitive value contains the query (case-insensitive). */
function matchPaths(value: unknown, query: string): Set<string> {
  const found = new Set<string>()
  const q = query.toLowerCase()
  if (!q) return found
  const walk = (v: unknown, path: string) => {
    if (isContainer(v)) {
      const arr = Array.isArray(v)
      for (const [k, child] of entriesOf(v)) {
        const childPath = `${path}/${k}`
        if (!arr && k.toLowerCase().includes(q)) found.add(childPath)
        walk(child, childPath)
      }
    } else {
      const s = v === null ? 'null' : String(v)
      if (s.toLowerCase().includes(q)) found.add(path)
    }
  }
  walk(value, 'root')
  return found
}

function allContainerPaths(value: unknown): Set<string> {
  const set = new Set<string>()
  const walk = (v: unknown, path: string) => {
    if (!isContainer(v)) return
    set.add(path)
    for (const [k, child] of entriesOf(v)) walk(child, `${path}/${k}`)
  }
  walk(value, 'root')
  return set
}

function highlight(text: string, query: string): ReactNode {
  if (!query) return text
  const q = query.toLowerCase()
  const lower = text.toLowerCase()
  const parts: ReactNode[] = []
  let i = 0
  while (i < text.length) {
    const idx = lower.indexOf(q, i)
    if (idx === -1) {
      parts.push(text.slice(i))
      break
    }
    if (idx > i) parts.push(text.slice(i, idx))
    parts.push(
      <mark key={idx} className="rounded bg-amber-200 px-0.5 dark:bg-amber-600/60 dark:text-white">
        {text.slice(idx, idx + q.length)}
      </mark>,
    )
    i = idx + q.length
  }
  return <>{parts}</>
}

function Primitive({ value, query }: { value: unknown; query: string }) {
  if (typeof value === 'string')
    return <span className="text-emerald-700 dark:text-emerald-300">"{highlight(value, query)}"</span>
  if (typeof value === 'number')
    return <span className="text-sky-700 dark:text-sky-300">{highlight(String(value), query)}</span>
  if (typeof value === 'boolean' || value === null)
    return <span className="text-purple-700 dark:text-purple-300">{highlight(String(value), query)}</span>
  return <span>{String(value)}</span>
}

function TreeNode({
  label,
  value,
  path,
  depth,
  expanded,
  toggle,
  query,
}: {
  label: string | null
  value: unknown
  path: string
  depth: number
  expanded: Set<string>
  toggle: (path: string) => void
  query: string
}) {
  const pad = { paddingLeft: `${depth * 14}px` }
  const key = label !== null && (
    <>
      <span className="text-slate-500 dark:text-slate-400">{highlight(label, query)}</span>
      <span className="text-slate-400">: </span>
    </>
  )

  if (!isContainer(value)) {
    return (
      <div style={pad} className="whitespace-pre-wrap py-px">
        {key}
        <Primitive value={value} query={query} />
      </div>
    )
  }

  const entries = entriesOf(value)
  const open = expanded.has(path)
  const bracket = Array.isArray(value) ? ['[', ']'] : ['{', '}']
  const count = Array.isArray(value) ? `${entries.length} item${entries.length === 1 ? '' : 's'}` : `${entries.length} key${entries.length === 1 ? '' : 's'}`

  return (
    <div>
      <div style={pad} className="cursor-pointer select-none py-px hover:bg-slate-100 dark:hover:bg-slate-800/60" onClick={() => toggle(path)}>
        <span className="inline-block w-3 text-slate-400">{open ? '▾' : '▸'}</span>
        {key}
        <span className="text-slate-400">
          {bracket[0]}
          {!open && (
            <>
              <span className="px-1 text-[10px] text-slate-400">{count}</span>
              {bracket[1]}
            </>
          )}
        </span>
      </div>
      {open && (
        <>
          {entries.map(([k, child]) => (
            <TreeNode
              key={k}
              label={k}
              value={child}
              path={`${path}/${k}`}
              depth={depth + 1}
              expanded={expanded}
              toggle={toggle}
              query={query}
            />
          ))}
          <div style={pad} className="text-slate-400">{bracket[1]}</div>
        </>
      )}
    </div>
  )
}

export default function JsonViewer({ text, maxHeightClass = 'max-h-[60vh]' }: { text: string; maxHeightClass?: string }) {
  const parsed = useMemo(() => parseLoose(text), [text])
  const tooBig = text.length > TREE_MAX
  const [mode, setMode] = useState<'tree' | 'raw'>(parsed.ok && !tooBig ? 'tree' : 'raw')
  const [query, setQuery] = useState('')
  const [expanded, setExpanded] = useState<Set<string>>(() => new Set(['root']))

  const value = parsed.ok ? parsed.value : null
  const matches = useMemo(() => (parsed.ok ? matchPaths(value, query) : new Set<string>()), [parsed.ok, value, query])

  // While searching, force-open every ancestor of a match so hits are visible; keep the user's own
  // toggles too. Base `expanded` is what manual toggles and expand/collapse-all persist into.
  const effectiveExpanded = useMemo(() => {
    if (!query) return expanded
    const set = new Set(expanded)
    for (const m of matches) for (const a of ancestorsOf(m)) set.add(a)
    set.add('root')
    return set
  }, [expanded, matches, query])

  const toggle = (path: string) =>
    setExpanded((prev) => {
      const next = new Set(prev)
      if (next.has(path)) next.delete(path)
      else next.add(path)
      return next
    })

  const pretty = useMemo(() => {
    if (!parsed.ok) return text
    try {
      return JSON.stringify(value, null, 2)
    } catch {
      return text
    }
  }, [parsed.ok, value, text])

  const canTree = parsed.ok && !tooBig
  const preClass = `${maxHeightClass} overflow-auto whitespace-pre-wrap rounded border border-slate-200 bg-slate-50 p-2 font-mono text-[11px] leading-relaxed text-slate-700 dark:border-slate-800 dark:bg-slate-950 dark:text-slate-300`

  return (
    <div className="space-y-2">
      <div className="flex flex-wrap items-center gap-2">
        {canTree && (
          <div className="flex overflow-hidden rounded border border-slate-300 text-[11px] dark:border-slate-700">
            {(['tree', 'raw'] as const).map((m) => (
              <button
                key={m}
                onClick={() => setMode(m)}
                className={`px-2 py-0.5 ${mode === m ? 'bg-primary text-white' : 'hover:bg-slate-100 dark:hover:bg-slate-800'}`}
              >
                {m === 'tree' ? 'Tree' : 'Raw'}
              </button>
            ))}
          </div>
        )}
        {mode === 'tree' && canTree && (
          <>
            <input
              aria-label="Find in JSON"
              placeholder="Find…"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              className="rounded border border-slate-300 px-2 py-0.5 text-[11px] dark:border-slate-700 dark:bg-slate-800"
            />
            {query && <span className="text-[11px] text-slate-500">{matches.size} match{matches.size === 1 ? '' : 'es'}</span>}
            <button
              onClick={() => setExpanded(allContainerPaths(value))}
              className="rounded border border-slate-300 px-2 py-0.5 text-[11px] hover:bg-slate-100 dark:border-slate-700 dark:hover:bg-slate-800"
            >
              Expand all
            </button>
            <button
              onClick={() => setExpanded(new Set(['root']))}
              className="rounded border border-slate-300 px-2 py-0.5 text-[11px] hover:bg-slate-100 dark:border-slate-700 dark:hover:bg-slate-800"
            >
              Collapse all
            </button>
          </>
        )}
      </div>

      {mode === 'tree' && canTree ? (
        <div className={`${preClass}`}>
          <TreeNode label={null} value={value} path="root" depth={0} expanded={effectiveExpanded} toggle={toggle} query={query} />
        </div>
      ) : (
        <>
          {tooBig && <p className="text-[11px] text-slate-500">Large payload: showing raw text. Use the browser find, or download.</p>}
          <pre className={preClass}>{pretty}</pre>
        </>
      )}
    </div>
  )
}
