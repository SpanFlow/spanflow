import type { CSSProperties, ReactNode } from 'react'
import { Minimize2 } from 'lucide-react'
import type { GroupBoundaryStyle } from './nodeGroups'

const FRAME: Record<GroupBoundaryStyle, string> = {
  dashed: 'rounded-2xl border border-dashed border-violet-400 bg-violet-50/40 dark:border-violet-500 dark:bg-violet-950/30',
  solid: 'rounded-2xl border-[1.5px] border-violet-400 bg-violet-50/20 dark:border-violet-500 dark:bg-violet-950/20',
  outline: 'rounded-2xl border border-violet-300 dark:border-violet-700',
}

const LABEL: Record<GroupBoundaryStyle, string> = {
  dashed: 'absolute left-2.5 -top-3 rounded-md border border-violet-400 bg-violet-50 px-2 py-0.5 dark:border-violet-500 dark:bg-violet-950',
  solid: 'absolute inset-x-0 top-0 h-[26px] rounded-t-[14px] border-b border-violet-300 bg-violet-50 px-2.5 dark:border-violet-700 dark:bg-violet-950',
  outline: 'absolute left-2.5 -top-5',
}

/**
 * An open group (E61): a frame around its members with the name on it. The label closes the group;
 * `extra` sits beside it (the Style button, in E61-T6). The frame itself lets clicks through to the canvas.
 */
export default function GroupBoundary({
  name,
  memberCount,
  look,
  width,
  height,
  onClose,
  extra,
  style,
}: {
  name: string
  memberCount: number
  look: GroupBoundaryStyle
  width: number
  height: number
  onClose: () => void
  extra?: ReactNode
  style?: CSSProperties
}) {
  return (
    <div
      data-testid="group-boundary"
      data-look={look}
      className={`pointer-events-none relative box-border ${FRAME[look]}`}
      style={{ width, height, ...style }}
    >
      <div className={`pointer-events-auto flex max-w-[calc(100%-1rem)] items-center gap-1.5 text-xs text-violet-900 dark:text-violet-100 ${LABEL[look]}`}>
        <button
          type="button"
          onClick={onClose}
          title={`Close ${name}`}
          aria-label={`Close group ${name}`}
          className="flex min-w-0 items-center gap-1.5 hover:underline"
        >
          <Minimize2 aria-hidden="true" className="h-3 w-3 shrink-0" />
          <span className="truncate">{name}</span>
          <span className="shrink-0 opacity-70">· {memberCount} nodes</span>
        </button>
        {extra}
      </div>
    </div>
  )
}
