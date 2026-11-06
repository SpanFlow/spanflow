import { useCallback, useMemo } from 'react'
import {
  createGroup, foldEdges, flowRank, groupBounds, nodeRect, removeGroup, renameGroup, resolveGroupStyle,
  setGroupCollapsed, setGroupStyle, type GroupStyle, type GroupableEdge, type NodeGroup, type PlacedNode, type Rect,
} from './nodeGroups'
import { closePlan, openPlan, prefersReducedMotion, type MotionMember } from './groupMotion'
import { useGroupMotion } from './useGroupMotion'

/** Each member's distance from the group's centre, and its step in the flow — what an animation needs. */
export function motionMembers(group: NodeGroup, nodes: readonly PlacedNode[], edges: readonly GroupableEdge[], bounds: Rect): MotionMember[] {
  const cx = bounds.x + bounds.width / 2
  const cy = bounds.y + bounds.height / 2
  const rank = flowRank(group.nodeIds, edges)
  const byId = new Map(nodes.map((n) => [n.id, n]))
  return group.nodeIds.flatMap((id) => {
    const n = byId.get(id)
    if (!n) return []
    const r = nodeRect(n)
    return [{ id, toCard: { dx: cx - (r.x + r.width / 2), dy: cy - (r.y + r.height / 2) }, rank: rank.get(id) ?? 0 }]
  })
}

/**
 * Everything a canvas needs to offer groups (E61): the edges to draw with collapsed groups folded, which
 * nodes to hide, each group's boundary and resolved style, and the actions — group, ungroup, rename, style,
 * open and close with their animation. The group list itself is owned by the canvas (it is saved with the
 * graph); this hook only reads it and hands back the next list through `onChange`.
 */
export function useNodeGroups<E extends GroupableEdge>({
  groups,
  onChange,
  nodes,
  edges,
  defaults,
  newId = () => crypto.randomUUID(),
}: {
  groups: readonly NodeGroup[]
  onChange: (next: NodeGroup[]) => void
  nodes: readonly PlacedNode[]
  edges: readonly E[]
  /** The person's "default look for new groups" (E61-T6); missing keys use the built-in default. */
  defaults?: Partial<GroupStyle> | null
  newId?: () => string
}) {
  const motion = useGroupMotion()

  const visibleEdges = useMemo(() => foldEdges(edges, groups), [edges, groups])

  const hiddenNodeIds = useMemo(() => {
    const hidden = new Set<string>()
    for (const g of groups) if (g.collapsed) g.nodeIds.forEach((id) => hidden.add(id))
    return hidden
  }, [groups])

  const layout = useMemo(() => groups.map((g) => ({
    group: g,
    style: resolveGroupStyle(g, defaults),
    bounds: groupBounds(nodes, g.nodeIds),
  })), [groups, nodes, defaults])

  const find = useCallback((id: string) => layout.find((l) => l.group.id === id), [layout])

  const group = useCallback((nodeIds: readonly string[], name: string) => {
    const id = newId()
    onChange(createGroup(groups, nodeIds, name, id))
    return id
  }, [groups, onChange, newId])

  const ungroup = useCallback((id: string) => onChange(removeGroup(groups, id)), [groups, onChange])
  const rename = useCallback((id: string, name: string) => onChange(renameGroup(groups, id, name)), [groups, onChange])
  const restyle = useCallback((id: string, patch: Partial<GroupStyle> | null) => onChange(setGroupStyle(groups, id, patch)), [groups, onChange])

  const open = useCallback((id: string) => {
    const l = find(id)
    if (!l || !l.group.collapsed) return
    onChange(setGroupCollapsed(groups, id, false))
    if (l.bounds) motion.play(id, openPlan(l.style, motionMembers(l.group, nodes, edges, l.bounds), prefersReducedMotion()))
  }, [find, groups, onChange, motion, nodes, edges])

  const close = useCallback((id: string) => {
    const l = find(id)
    if (!l || l.group.collapsed) return
    const fold = () => onChange(setGroupCollapsed(groups, id, true))
    if (!l.bounds) return fold()
    motion.play(id, closePlan(l.style, motionMembers(l.group, nodes, edges, l.bounds), prefersReducedMotion()), fold)
  }, [find, groups, onChange, motion, nodes, edges])

  return { visibleEdges, hiddenNodeIds, layout, group, ungroup, rename, restyle, open, close, frame: motion.frame, isPlaying: motion.isPlaying }
}
