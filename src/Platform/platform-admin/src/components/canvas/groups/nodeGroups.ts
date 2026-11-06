/**
 * Node groups (E61): a named set of nodes that a canvas can fold into one card and bloom back open.
 * A group is a view only — runtimes ignore it — so everything here is pure bookkeeping over ids,
 * shared by the pipeline, chat flow and mapping canvases.
 */

export type GroupOpenStyle = 'spiral' | 'burst' | 'unfold' | 'fan'
export type GroupCardStyle = 'count' | 'stacked' | 'preview'
export type GroupBoundaryStyle = 'dashed' | 'solid' | 'outline'

export interface GroupStyle {
  open: GroupOpenStyle
  card: GroupCardStyle
  boundary: GroupBoundaryStyle
  /** Multiplies animation speed: 2 plays twice as fast. */
  speed: number
}

export interface NodeGroup {
  id: string
  name: string
  nodeIds: string[]
  collapsed: boolean
  /** Only what this group overrides (D61.3); missing keys follow the person's defaults. */
  style?: Partial<GroupStyle>
}

export const BUILT_IN_GROUP_STYLE: GroupStyle = { open: 'spiral', card: 'count', boundary: 'dashed', speed: 1 }

export const GROUP_SPEED = { min: 0.25, max: 3, step: 0.25 } as const

export const OPEN_STYLES: GroupOpenStyle[] = ['spiral', 'burst', 'unfold', 'fan']
export const CARD_STYLES: GroupCardStyle[] = ['count', 'stacked', 'preview']
export const BOUNDARY_STYLES: GroupBoundaryStyle[] = ['dashed', 'solid', 'outline']

/** What each choice is called wherever a person picks one (a group's Style popover, profile settings). */
export const STYLE_LABELS = {
  open: { spiral: 'Petal spiral', burst: 'Flower burst', unfold: 'Unfold in place', fan: 'Fan out along flow' },
  card: { count: 'Name and count', stacked: 'Stacked cards', preview: 'Mini preview' },
  boundary: { dashed: 'Dashed, tinted', solid: 'Solid frame', outline: 'Outline only' },
} as const satisfies {
  open: Record<GroupOpenStyle, string>
  card: Record<GroupCardStyle, string>
  boundary: Record<GroupBoundaryStyle, string>
}

/** Snaps to the slider's step and keeps it in range; anything unreadable is 1×. */
export function clampSpeed(speed: unknown): number {
  const n = typeof speed === 'number' && Number.isFinite(speed) ? speed : 1
  const snapped = Math.round(n / GROUP_SPEED.step) * GROUP_SPEED.step
  return Math.min(GROUP_SPEED.max, Math.max(GROUP_SPEED.min, snapped))
}

function pick<T extends string>(value: unknown, allowed: readonly T[]): T | undefined {
  return allowed.includes(value as T) ? (value as T) : undefined
}

/**
 * The style a group plays with: its own value per key, else the person's default, else the built-in
 * one (D61.3). Unknown values — a style a newer build added, or a typo in an imported bundle — fall
 * through instead of breaking the canvas.
 */
export function resolveGroupStyle(group: Pick<NodeGroup, 'style'>, defaults?: Partial<GroupStyle> | null): GroupStyle {
  const own = group.style ?? {}
  const mine = defaults ?? {}
  return {
    open: pick(own.open, OPEN_STYLES) ?? pick(mine.open, OPEN_STYLES) ?? BUILT_IN_GROUP_STYLE.open,
    card: pick(own.card, CARD_STYLES) ?? pick(mine.card, CARD_STYLES) ?? BUILT_IN_GROUP_STYLE.card,
    boundary: pick(own.boundary, BOUNDARY_STYLES) ?? pick(mine.boundary, BOUNDARY_STYLES) ?? BUILT_IN_GROUP_STYLE.boundary,
    speed: clampSpeed(own.speed ?? mine.speed ?? BUILT_IN_GROUP_STYLE.speed),
  }
}

/**
 * Makes a saved or imported group list safe to draw (D61.2): unknown node ids dropped, a node claimed
 * twice stays in the first group, groups left with fewer than two nodes removed, blank names named.
 */
export function cleanGroups(groups: readonly NodeGroup[] | null | undefined, nodeIds: Iterable<string>): NodeGroup[] {
  const known = new Set(nodeIds)
  const claimed = new Set<string>()
  const seenIds = new Set<string>()
  const out: NodeGroup[] = []
  for (const g of groups ?? []) {
    if (!g || typeof g.id !== 'string' || !g.id || seenIds.has(g.id)) continue
    const members = (Array.isArray(g.nodeIds) ? g.nodeIds : []).filter((id) => known.has(id) && !claimed.has(id))
    const unique = [...new Set(members)]
    if (unique.length < 2) continue
    unique.forEach((id) => claimed.add(id))
    seenIds.add(g.id)
    out.push({
      id: g.id,
      name: typeof g.name === 'string' && g.name.trim() ? g.name.trim() : 'Group',
      nodeIds: unique,
      collapsed: g.collapsed === true,
      ...(g.style && Object.keys(g.style).length ? { style: { ...g.style } } : {}),
    })
  }
  return out
}

/**
 * Adds a group over `nodeIds`. Nodes already in another group leave it (one group per node in v1), and a
 * group that drops below two nodes goes away. A new group carries no style, so it follows the person's
 * defaults; it starts collapsed, which is the point of grouping.
 */
export function createGroup(
  groups: readonly NodeGroup[],
  nodeIds: readonly string[],
  name: string,
  id: string,
): NodeGroup[] {
  const members = [...new Set(nodeIds)]
  if (members.length < 2) return [...groups]
  const taken = new Set(members)
  const rest = groups
    .map((g) => ({ ...g, nodeIds: g.nodeIds.filter((n) => !taken.has(n)) }))
    .filter((g) => g.nodeIds.length >= 2)
  return [...rest, { id, name: name.trim() || 'Group', nodeIds: members, collapsed: true }]
}

export function removeGroup(groups: readonly NodeGroup[], id: string): NodeGroup[] {
  return groups.filter((g) => g.id !== id)
}

export function renameGroup(groups: readonly NodeGroup[], id: string, name: string): NodeGroup[] {
  const trimmed = name.trim()
  if (!trimmed) return [...groups]
  return groups.map((g) => (g.id === id ? { ...g, name: trimmed } : g))
}

export function setGroupCollapsed(groups: readonly NodeGroup[], id: string, collapsed: boolean): NodeGroup[] {
  return groups.map((g) => (g.id === id ? { ...g, collapsed } : g))
}

/** Writes overrides onto one group; `null` clears them all ("Use defaults"). */
export function setGroupStyle(groups: readonly NodeGroup[], id: string, patch: Partial<GroupStyle> | null): NodeGroup[] {
  return groups.map((g) => {
    if (g.id !== id) return g
    if (patch === null) {
      const { style: _dropped, ...rest } = g
      return rest
    }
    return { ...g, style: { ...g.style, ...patch } }
  })
}

/** Node id → the group it is in. */
export function groupIndex(groups: readonly NodeGroup[]): Map<string, NodeGroup> {
  const map = new Map<string, NodeGroup>()
  for (const g of groups) for (const n of g.nodeIds) if (!map.has(n)) map.set(n, g)
  return map
}

/** The id a collapsed group's card has on the canvas; edges to members attach to it. */
export const groupCardId = (groupId: string) => `group:${groupId}`

export interface GroupableEdge {
  id: string
  source: string
  target: string
  sourceHandle?: string | null
  targetHandle?: string | null
  data?: Record<string, unknown>
}

/** What a merged edge stands for: how many real edges, and which. */
export interface GroupEdgeInfo {
  count: number
  edgeIds: string[]
}

/**
 * Edges as a canvas shows them with collapsed groups folded (D61.4). Edges inside one collapsed group are
 * hidden; an edge with an end in a collapsed group attaches to that group's card (handles cleared — a card
 * has one way in and one out); several edges that now join the same two things become one, carrying
 * `data.groupEdge` with the count. Edges that touch no collapsed group come back untouched.
 */
export function foldEdges<E extends GroupableEdge>(edges: readonly E[], groups: readonly NodeGroup[]): E[] {
  const folded = new Map<string, NodeGroup>()
  for (const g of groups) if (g.collapsed) for (const n of g.nodeIds) if (!folded.has(n)) folded.set(n, g)
  if (folded.size === 0) return [...edges]

  const out: E[] = []
  const merged = new Map<string, { edge: E; info: GroupEdgeInfo }>()
  for (const e of edges) {
    const gs = folded.get(e.source)
    const gt = folded.get(e.target)
    if (!gs && !gt) {
      out.push(e)
      continue
    }
    if (gs && gt && gs.id === gt.id) continue
    const source = gs ? groupCardId(gs.id) : e.source
    const target = gt ? groupCardId(gt.id) : e.target
    // The kept end's handle still matters (a condition's "true" port), so it is part of the key.
    const sourceHandle = gs ? null : (e.sourceHandle ?? null)
    const targetHandle = gt ? null : (e.targetHandle ?? null)
    const key = `${source}|${sourceHandle ?? ''}->${target}|${targetHandle ?? ''}`
    const seen = merged.get(key)
    if (seen) {
      seen.info.count += 1
      seen.info.edgeIds.push(e.id)
      continue
    }
    const info: GroupEdgeInfo = { count: 1, edgeIds: [e.id] }
    const edge = { ...e, id: `group-edge:${key}`, source, target, sourceHandle, targetHandle, data: { ...e.data, groupEdge: info } }
    merged.set(key, { edge, info })
    out.push(edge)
  }
  return out
}

export interface Rect {
  x: number
  y: number
  width: number
  height: number
}

export interface PlacedNode {
  id: string
  position: { x: number; y: number }
  width?: number
  height?: number
  measured?: { width?: number; height?: number }
}

/** Size used for a node React Flow has not measured yet. */
export const FALLBACK_NODE_SIZE = { width: 180, height: 60 }

export function nodeRect(n: PlacedNode): Rect {
  return {
    x: n.position.x,
    y: n.position.y,
    width: n.measured?.width ?? n.width ?? FALLBACK_NODE_SIZE.width,
    height: n.measured?.height ?? n.height ?? FALLBACK_NODE_SIZE.height,
  }
}

/** The boundary around a group's members: their bounding box plus padding, with room on top for the label. */
export function groupBounds(nodes: readonly PlacedNode[], memberIds: readonly string[], padding = 24, labelSpace = 16): Rect | null {
  const ids = new Set(memberIds)
  const rects = nodes.filter((n) => ids.has(n.id)).map(nodeRect)
  if (rects.length === 0) return null
  const minX = Math.min(...rects.map((r) => r.x))
  const minY = Math.min(...rects.map((r) => r.y))
  const maxX = Math.max(...rects.map((r) => r.x + r.width))
  const maxY = Math.max(...rects.map((r) => r.y + r.height))
  return { x: minX - padding, y: minY - padding - labelSpace, width: maxX - minX + padding * 2, height: maxY - minY + padding * 2 + labelSpace }
}

export const CARD_SIZE = { minWidth: 150, maxWidth: 240, height: 60 } as const

/** A collapsed card is as wide as its group, within limits (D61.6). */
export function cardWidth(bounds: Rect | null): number {
  const w = bounds?.width ?? CARD_SIZE.minWidth
  return Math.round(Math.min(CARD_SIZE.maxWidth, Math.max(CARD_SIZE.minWidth, w)))
}

/**
 * Each member's step in the flow inside its group: 0 for members nothing in the group feeds, then one more
 * per edge (the longest path). "Fan out along flow" opens step 0 first. Cycles stop at the members already
 * counted rather than looping.
 */
export function flowRank(memberIds: readonly string[], edges: readonly Pick<GroupableEdge, 'source' | 'target'>[]): Map<string, number> {
  const members = new Set(memberIds)
  const inner = edges.filter((e) => members.has(e.source) && members.has(e.target) && e.source !== e.target)
  const rank = new Map<string, number>(memberIds.map((id) => [id, 0]))
  for (let pass = 0; pass < memberIds.length; pass++) {
    let changed = false
    for (const e of inner) {
      const next = (rank.get(e.source) ?? 0) + 1
      if (next > (rank.get(e.target) ?? 0) && next < memberIds.length) {
        rank.set(e.target, next)
        changed = true
      }
    }
    if (!changed) break
  }
  return rank
}
