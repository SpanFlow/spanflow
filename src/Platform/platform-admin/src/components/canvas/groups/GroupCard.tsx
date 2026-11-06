import type { CSSProperties } from 'react'
import { Boxes } from 'lucide-react'
import { CARD_SIZE, type GroupCardStyle } from './nodeGroups'

/** A collapsed group's members, summed for the card while a run or trace is shown (D61.8). */
export interface GroupRunState {
  running: number
  failed: number
  succeeded: number
}

const face = 'rounded-xl border border-violet-400 bg-violet-50 text-violet-900 dark:border-violet-500 dark:bg-violet-950 dark:text-violet-100'

/** Up to four dots in a square, joined like a tiny graph: what the group holds, at a glance. */
function MiniPreview({ count }: { count: number }) {
  const dots = [[8, 8], [22, 8], [8, 22], [22, 22]].slice(0, Math.max(2, Math.min(4, count)))
  return (
    <svg data-testid="group-card-preview" width="24" height="24" viewBox="0 0 30 30" aria-hidden="true" className="shrink-0">
      <path d="M8 8L22 8M8 22L22 22M8 8L8 22" stroke="currentColor" strokeWidth="1" fill="none" opacity="0.6" />
      {dots.map(([cx, cy]) => <circle key={`${cx}-${cy}`} cx={cx} cy={cy} r="3" fill="currentColor" />)}
    </svg>
  )
}

function runLine(state: GroupRunState | undefined): string | null {
  if (!state) return null
  if (state.failed > 0) return `${state.failed} failed`
  if (state.running > 0) return 'running'
  if (state.succeeded > 0) return 'all done'
  return null
}

/**
 * A collapsed group (E61): its name and node count in one of three looks. The text never leaves the card
 * (D61.6): the name wraps to two lines and then ends in an ellipsis, the count is one line, and the full
 * name is in the tooltip.
 */
export default function GroupCard({
  name,
  memberCount,
  look,
  width = CARD_SIZE.minWidth,
  runState,
  onOpen,
  style,
}: {
  name: string
  memberCount: number
  look: GroupCardStyle
  width?: number
  runState?: GroupRunState
  onOpen: () => void
  style?: CSSProperties
}) {
  const run = runLine(runState)
  const nodes = `${memberCount} node${memberCount === 1 ? '' : 's'}`
  return (
    <button
      type="button"
      data-testid="group-card"
      data-look={look}
      title={`${name} (${nodes}): open`}
      aria-label={`Open group ${name}, ${nodes}${run ? `, ${run}` : ''}`}
      onClick={onOpen}
      className="relative block cursor-pointer text-left"
      style={{ width, height: CARD_SIZE.height, ...style }}
    >
      {look === 'stacked' && (
        <>
          <span aria-hidden="true" className={`absolute inset-0 translate-x-2 translate-y-2 ${face}`} />
          <span aria-hidden="true" className={`absolute inset-0 translate-x-1 translate-y-1 ${face}`} />
        </>
      )}
      <span className={`absolute inset-0 flex items-center gap-2 overflow-hidden px-2.5 ${face}`}>
        {look === 'preview' ? <MiniPreview count={memberCount} /> : <Boxes aria-hidden="true" className="h-[18px] w-[18px] shrink-0" />}
        <span className="min-w-0 flex-1">
          <span data-testid="group-card-name" className="line-clamp-2 break-words text-[13px] leading-tight font-medium">{name}</span>
          <span className="block truncate text-[11px] opacity-80">
            {nodes}
            {run && <span className={runState && runState.failed > 0 ? 'text-red-700 dark:text-red-300' : ''}> · {run}</span>}
          </span>
        </span>
      </span>
    </button>
  )
}
