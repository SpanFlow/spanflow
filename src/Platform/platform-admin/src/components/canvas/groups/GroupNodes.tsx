import { useEffect, useRef, useState, type CSSProperties } from 'react'
import { Handle, Position, type Node, type NodeProps } from '@xyflow/react'
import { Palette } from 'lucide-react'
import GroupBoundary from './GroupBoundary'
import GroupCard, { type GroupRunState } from './GroupCard'
import GroupStyleFields from './GroupStyleFields'
import type { GroupBoundaryStyle, GroupCardStyle, GroupStyle } from './nodeGroups'

const handleClass = '!h-2.5 !w-2.5 !bg-violet-400 !border-2 !border-white dark:!border-slate-900'

/** How long a click waits to see whether it is the first half of a double-click (rename). */
export const RENAME_CLICK_DELAY = 220

/**
 * One click opens or closes a group, two rename it (E61-T3). Both land on the same label, so a single
 * click waits a moment before acting; the second click of a double-click cancels it.
 */
function useClickOrRename(onClick: () => void, onRename: () => void) {
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null)
  const click = () => {
    if (timer.current) clearTimeout(timer.current)
    timer.current = setTimeout(() => {
      timer.current = null
      onClick()
    }, RENAME_CLICK_DELAY)
  }
  const doubleClick = () => {
    if (timer.current) clearTimeout(timer.current)
    timer.current = null
    onRename()
  }
  return { click, doubleClick }
}

export type GroupCardNodeData = {
  name: string
  memberCount: number
  look: GroupCardStyle
  width: number
  runState?: GroupRunState
  motion?: CSSProperties
  onOpen: () => void
  onRename: () => void
}

export type GroupFrameNodeData = {
  name: string
  memberCount: number
  look: GroupBoundaryStyle
  width: number
  height: number
  motion?: CSSProperties
  onClose: () => void
  onRename: () => void
  /** The look in effect (own values, else the viewer's defaults, else built-in). */
  style: GroupStyle
  /** Whether this group overrides anything; "Use defaults" is offered only then. */
  hasOwnStyle: boolean
  /** Writes onto this group only; null clears its own values (D61.3). */
  onRestyle: (patch: Partial<GroupStyle> | null) => void
}

/**
 * A group's Style popover (E61-T6, D61.3): the four choices, written onto this group only, and **Use
 * defaults** to go back to following the person's default look. It sits inside a React Flow node, so it is
 * marked `nodrag nopan nowheel`: a slider dragged here moves the slider, not the group.
 */
function StylePopover({ name, style, hasOwnStyle, onRestyle }: Pick<GroupFrameNodeData, 'name' | 'style' | 'hasOwnStyle' | 'onRestyle'>) {
  const [open, setOpen] = useState(false)
  const box = useRef<HTMLSpanElement>(null)

  useEffect(() => {
    if (!open) return
    const away = (e: MouseEvent) => {
      if (box.current && !box.current.contains(e.target as globalThis.Node)) setOpen(false)
    }
    const escape = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false)
    }
    document.addEventListener('mousedown', away)
    document.addEventListener('keydown', escape)
    return () => {
      document.removeEventListener('mousedown', away)
      document.removeEventListener('keydown', escape)
    }
  }, [open])

  return (
    <span ref={box} className="nodrag nopan nowheel relative shrink-0">
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        title={`Style ${name}`}
        aria-label={`Style group ${name}`}
        aria-expanded={open}
        className="flex items-center rounded p-0.5 hover:bg-violet-100 dark:hover:bg-violet-900"
      >
        <Palette aria-hidden="true" className="h-3 w-3" />
      </button>
      {open && (
        <div
          role="dialog"
          aria-label={`Style of ${name}`}
          onDoubleClick={(e) => e.stopPropagation()}
          className="absolute left-0 top-6 z-30 flex w-56 flex-col gap-2 rounded-lg border border-slate-200 bg-white p-2.5 text-left shadow-lg dark:border-slate-700 dark:bg-slate-900"
        >
          <GroupStyleFields value={style} onChange={(patch) => onRestyle(patch)} idPrefix={`group-style-${name}`} />
          <div className="flex items-center justify-between gap-2 border-t border-slate-100 pt-2 text-[11px] dark:border-slate-800">
            <span className="text-slate-400">{hasOwnStyle ? 'This group only' : 'Following your default look'}</span>
            {hasOwnStyle && (
              <button type="button" onClick={() => onRestyle(null)} className="font-medium text-violet-700 hover:underline dark:text-violet-300">
                Use defaults
              </button>
            )}
          </div>
        </div>
      )}
    </span>
  )
}

/**
 * A collapsed group on the pipeline canvas: the shared card with one way in and one way out, which is
 * where the edges of its members attach while it is folded (D61.4).
 */
export function GroupCardNode({ data }: NodeProps<Node<GroupCardNodeData, 'groupCard'>>) {
  const { click, doubleClick } = useClickOrRename(data.onOpen, data.onRename)
  return (
    <div onDoubleClick={doubleClick}>
      <Handle type="target" position={Position.Left} className={handleClass} />
      <GroupCard
        name={data.name}
        memberCount={data.memberCount}
        look={data.look}
        width={data.width}
        runState={data.runState}
        onOpen={click}
        style={data.motion}
      />
      <Handle type="source" position={Position.Right} className={handleClass} />
    </div>
  )
}

/** An open group: the boundary around its members. Dragging it moves them; its label folds it up. */
export function GroupFrameNode({ data }: NodeProps<Node<GroupFrameNodeData, 'groupFrame'>>) {
  const { click, doubleClick } = useClickOrRename(data.onClose, data.onRename)
  return (
    <div onDoubleClick={doubleClick}>
      <GroupBoundary
        name={data.name}
        memberCount={data.memberCount}
        look={data.look}
        width={data.width}
        height={data.height}
        onClose={click}
        style={data.motion}
        extra={<StylePopover name={data.name} style={data.style} hasOwnStyle={data.hasOwnStyle} onRestyle={data.onRestyle} />}
      />
    </div>
  )
}

/** Ids the canvas gives a group's card and frame; neither is a pipeline node. */
export const groupFrameId = (groupId: string) => `group-frame:${groupId}`
export const isGroupNodeId = (id: string) => id.startsWith('group:') || id.startsWith('group-frame:')
export const groupIdOf = (id: string) => id.slice(id.indexOf(':') + 1)
