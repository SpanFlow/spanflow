import {
  useCallback,
  useMemo,
  useRef,
  useState,
  type KeyboardEvent as ReactKeyboardEvent,
  type MouseEvent as ReactMouseEvent,
} from 'react'
import { useReactFlow, type Edge, type Node, type NodeChange } from '@xyflow/react'
import { cardWidth, groupCardId, groupIndex, type GroupEdgeInfo, type GroupStyle, type NodeGroup } from './nodeGroups'
import type { MotionFrame } from './groupMotion'
import type { GroupRunState } from './GroupCard'
import { GroupCardNode, GroupFrameNode, groupFrameId, groupIdOf, isGroupNodeId } from './GroupNodes'
import { useNodeGroups } from './useNodeGroups'
import { usePreferencesStore } from '../../../stores/preferencesStore'

/** The React Flow node types a canvas adds for its groups' cards and frames. */
export const GROUP_NODE_TYPES = { groupCard: GroupCardNode, groupFrame: GroupFrameNode }

/** Room above a group's members for the boundary label, as `groupBounds` leaves it. */
const LABEL_SPACE = 16

/** What a member node draws while its group opens or closes: an offset from where it rests, and a look. */
function memberMotion(f: MotionFrame | undefined) {
  if (!f) return null
  return { dx: f.dx, dy: f.dy, style: { opacity: f.opacity, scale: String(f.scale), rotate: `${f.rotate}deg` } }
}

/** Which groups exist and which are folded: when this changes, the layout does (D61.5). */
const groupShape = (groups: readonly NodeGroup[]) => JSON.stringify(groups.map((g) => [g.id, g.collapsed, g.nodeIds]))

/**
 * Node groups on an auto-laid-out canvas (E61-T3, T4): the pipeline canvas and the chat flow canvas share
 * all of this. The canvas owns the group list (it is saved with its graph) and the layout; this hook
 * turns them into what React Flow draws — the nodes minus those folded away, a frame behind each open
 * group, a card for each folded one, members offset while they animate, folded edges — and handles what
 * a person does to a group: group the selection (button, Ctrl+G, right-click), open, close, rename,
 * ungroup, drag a card or frame, and "show me this node" from a run log, which opens its group first.
 *
 * Every derived node is typed as the canvas's own node type for its handlers' sake: a card or frame is
 * never deletable, and the handlers this hook wraps check `isGroupNodeId` first.
 */
export function useCanvasGroups<N extends Node>({
  enabled,
  groups,
  onGroupsChange,
  relayout,
  nodes,
  edges,
  setNodes,
  onNodesChange,
  runStateOf,
  onSelectNode,
  onWarning,
  defaults,
}: {
  enabled: boolean
  groups: NodeGroup[]
  onGroupsChange: (next: NodeGroup[]) => void
  /** Lays the canvas out again for a new set of groups (a fold changes which slots there are). */
  relayout: (next: readonly NodeGroup[]) => void
  nodes: N[]
  edges: Edge[]
  setNodes: (update: (nodes: N[]) => N[]) => void
  onNodesChange: (changes: NodeChange<N>[]) => void
  /** A folded group's members summed for its card while a run or walk is shown (D61.8). */
  runStateOf?: (members: N[]) => GroupRunState | undefined
  /** Selects a node in the canvas's own sense (inspector, dock tab). */
  onSelectNode: (id: string) => void
  onWarning?: (message: string | null) => void
  /**
   * The look groups without their own style follow; a group's own style wins, key by key (D61.3). Left out,
   * it is the viewer's "Default look for new groups" from profile settings (E61-T6).
   */
  defaults?: Partial<GroupStyle> | null
}) {
  // Loaded by the app shell with the rest of the person's preferences (Layout), like Easy / Manage.
  const myDefaults = usePreferencesStore((s) => s.groupStyle)

  const { fitView } = useReactFlow()
  const [menu, setMenu] = useState<{ x: number; y: number; groupId?: string } | null>(null)
  const canvasRef = useRef<HTMLDivElement>(null)

  const kit = useNodeGroups({
    groups,
    onChange: (next) => {
      const moved = groupShape(next) !== groupShape(groups)
      onGroupsChange(next)
      if (moved) relayout(next)
    },
    nodes,
    edges,
    defaults: defaults ?? myDefaults,
  })

  const selectedNodeIds = nodes.filter((n) => n.selected).map((n) => n.id)

  function groupSelected() {
    setMenu(null)
    if (selectedNodeIds.length < 2) {
      onWarning?.('Select two or more nodes to group them: Shift-drag a box round them, or Ctrl-click each one.')
      return
    }
    const name = window.prompt('Name this group:', 'Group')
    if (name === null) return
    onWarning?.(null)
    kit.group(selectedNodeIds, name)
  }

  function renameGroup(id: string) {
    setMenu(null)
    const current = groups.find((g) => g.id === id)
    if (!current) return
    const name = window.prompt('Rename this group:', current.name)
    if (name !== null) kit.rename(id, name)
  }

  function ungroup(id: string) {
    setMenu(null)
    kit.ungroup(id)
  }

  /**
   * Shows one node, wherever it is (D61.8): a node folded into a group opens the group first, then the
   * node is selected and the view moves to it.
   */
  function focusNode(id: string) {
    const owner = groupIndex(groups).get(id)
    if (owner?.collapsed) kit.open(owner.id)
    setNodes((nds) => nds.map((n) => ({ ...n, selected: n.id === id })))
    onSelectNode(id)
    // After React Flow has the opened group's positions.
    setTimeout(() => fitView({ nodes: [{ id }], duration: 300, maxZoom: 1.25, padding: 0.4 }), 60)
  }

  const displayNodes = useMemo(() => {
    const out: Node[] = []
    for (const { group: g, style, bounds } of kit.layout) {
      if (!bounds) continue
      if (g.collapsed) {
        const width = cardWidth(bounds)
        const cx = bounds.x + bounds.width / 2
        const cy = bounds.y + bounds.height / 2 + LABEL_SPACE / 2
        const f = kit.frame(g.id, 'card')
        const members = nodes.filter((n) => g.nodeIds.includes(n.id))
        out.push({
          id: groupCardId(g.id),
          type: 'groupCard',
          position: { x: cx - width / 2, y: cy - 30 },
          selectable: false,
          deletable: false,
          data: {
            name: g.name,
            memberCount: g.nodeIds.length,
            look: style.card,
            width,
            runState: runStateOf?.(members),
            motion: f ? { transform: `scale(${f.scale})`, opacity: f.opacity } : undefined,
            onOpen: () => kit.open(g.id),
            onRename: () => renameGroup(g.id),
          },
        })
      } else {
        const f = kit.frame(g.id, 'boundary')
        out.push({
          id: groupFrameId(g.id),
          type: 'groupFrame',
          position: { x: bounds.x, y: bounds.y },
          selectable: false,
          deletable: false,
          zIndex: -1,
          data: {
            name: g.name,
            memberCount: g.nodeIds.length,
            look: style.boundary,
            width: bounds.width,
            height: bounds.height,
            motion: f ? { transform: `scale(${f.scale})`, opacity: f.opacity } : undefined,
            onClose: () => kit.close(g.id),
            onRename: () => renameGroup(g.id),
            style,
            hasOwnStyle: !!g.style && Object.keys(g.style).length > 0,
            onRestyle: (patch: Partial<GroupStyle> | null) => kit.restyle(g.id, patch),
          },
        })
      }
    }
    const ownerOf = groupIndex(groups)
    for (const n of nodes) {
      if (kit.hiddenNodeIds.has(n.id)) continue
      const owner = ownerOf.get(n.id)
      const m = owner ? memberMotion(kit.frame(owner.id, `node:${n.id}`)) : null
      out.push(m ? { ...n, position: { x: n.position.x + m.dx, y: n.position.y + m.dy }, style: { ...n.style, ...m.style } } : n)
    }
    return out as N[]
    // renameGroup and runStateOf read state that is a dependency already.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [nodes, groups, kit.layout, kit.hiddenNodeIds, kit.frame, runStateOf])

  /** Folded edges with a count where several became one (D61.4). */
  const displayEdges = useMemo(
    () =>
      kit.visibleEdges.map((e) => {
        const info = (e.data as { groupEdge?: GroupEdgeInfo } | undefined)?.groupEdge
        if (!info || info.count < 2) return e
        const port = typeof e.label === 'string' && e.label ? `${e.label} ` : ''
        return { ...e, label: `${port}×${info.count}` }
      }),
    [kit.visibleEdges],
  )

  /**
   * React Flow's changes, with the group layer peeled off: dragging a card or a frame moves every member
   * by the same amount, and anything else a card or frame reports (its size, a hover) is ours alone.
   */
  const handleNodesChange = useCallback(
    (changes: NodeChange<N>[]) => {
      const own: NodeChange<N>[] = []
      for (const c of changes) {
        if (!('id' in c) || !isGroupNodeId(c.id)) {
          own.push(c)
          continue
        }
        if (c.type !== 'position' || !c.position) continue
        const shown = displayNodes.find((n) => n.id === c.id)
        const g = groups.find((x) => x.id === groupIdOf(c.id))
        if (!shown || !g) continue
        const dx = c.position.x - shown.position.x
        const dy = c.position.y - shown.position.y
        if (!dx && !dy) continue
        const members = new Set(g.nodeIds)
        setNodes((nds) => nds.map((n) => (members.has(n.id) ? { ...n, position: { x: n.position.x + dx, y: n.position.y + dy } } : n)))
      }
      if (own.length) onNodesChange(own)
    },
    [displayNodes, groups, onNodesChange, setNodes],
  )

  function openMenu(event: ReactMouseEvent, groupId?: string) {
    event.preventDefault()
    const box = canvasRef.current?.getBoundingClientRect()
    setMenu({ x: event.clientX - (box?.left ?? 0), y: event.clientY - (box?.top ?? 0), groupId })
  }

  function onKeyDown(event: ReactKeyboardEvent) {
    if (!enabled) return
    const target = event.target as HTMLElement
    if (target.closest('input, textarea, select, [contenteditable="true"]')) return
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'g') {
      event.preventDefault()
      groupSelected()
    }
    if (event.key === 'Escape') setMenu(null)
  }

  const menuGroup = menu?.groupId ? groups.find((g) => g.id === menu.groupId) : undefined
  const item = 'px-3 py-1 text-left hover:bg-slate-100 disabled:opacity-50 dark:hover:bg-slate-700'
  const menuElement = menu && enabled ? (
    <div
      role="menu"
      aria-label="Group actions"
      className="absolute z-20 flex min-w-[10rem] flex-col rounded border border-slate-200 bg-white py-1 text-xs shadow-lg dark:border-slate-700 dark:bg-slate-800"
      style={{ left: menu.x, top: menu.y }}
    >
      {menuGroup ? (
        <>
          <button role="menuitem" className={item}
            onClick={() => { setMenu(null); if (menuGroup.collapsed) kit.open(menuGroup.id); else kit.close(menuGroup.id) }}>
            {menuGroup.collapsed ? 'Open group' : 'Close group'}
          </button>
          <button role="menuitem" className={item} onClick={() => renameGroup(menuGroup.id)}>Rename…</button>
          <button role="menuitem" className={item} onClick={() => ungroup(menuGroup.id)}>Ungroup</button>
        </>
      ) : (
        <button role="menuitem" disabled={selectedNodeIds.length < 2} className={item} onClick={groupSelected}>
          Group {selectedNodeIds.length >= 2 ? `${selectedNodeIds.length} nodes` : 'selected nodes'}…
        </button>
      )}
    </div>
  ) : null

  /** Props for the ReactFlow element: the drawn nodes and edges and the group-aware handlers. */
  const flowProps = {
    nodes: displayNodes,
    edges: displayEdges,
    onNodesChange: handleNodesChange,
    onNodeContextMenu: enabled
      ? (event: ReactMouseEvent, node: Node) => openMenu(event, isGroupNodeId(node.id) ? groupIdOf(node.id) : undefined)
      : undefined,
    onSelectionContextMenu: enabled ? (event: ReactMouseEvent) => openMenu(event) : undefined,
  }

  return {
    kit,
    flowProps,
    menuElement,
    canvasRef,
    onKeyDown,
    closeMenu: () => setMenu(null),
    selectedNodeIds,
    groupSelected,
    focusNode,
  }
}

export { isGroupNodeId }

/** A folded edge stands for several real ones, so it has no single port to edit or delete. */
export const isGroupEdgeId = (id: string) => id.startsWith('group-edge:')
