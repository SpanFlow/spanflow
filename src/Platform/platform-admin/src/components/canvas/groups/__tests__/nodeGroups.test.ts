import { describe, expect, it } from 'vitest'
import {
  BUILT_IN_GROUP_STYLE, cardWidth, clampSpeed, cleanGroups, createGroup, flowRank, foldEdges, groupBounds,
  groupCardId, renameGroup, resolveGroupStyle, setGroupStyle, type GroupableEdge, type NodeGroup,
} from '../nodeGroups'

const g = (id: string, nodeIds: string[], over: Partial<NodeGroup> = {}): NodeGroup => ({ id, name: id, nodeIds, collapsed: true, ...over })
const e = (id: string, source: string, target: string, sourceHandle?: string): GroupableEdge => ({ id, source, target, sourceHandle })

describe('group style', () => {
  it('takes each setting from the group, then the person, then the built-in default', () => {
    const style = resolveGroupStyle({ style: { open: 'burst' } }, { open: 'fan', card: 'stacked' })
    expect(style).toEqual({ open: 'burst', card: 'stacked', boundary: 'dashed', speed: 1 })
  })

  it('keeps every group on its own style when the defaults change (the first demo got this wrong)', () => {
    const a = g('a', ['1', '2'], { style: { card: 'preview' } })
    const b = g('b', ['3', '4'], { style: { open: 'burst', boundary: 'solid', speed: 1.5 } })
    const defaults = { open: 'unfold' as const, card: 'stacked' as const, boundary: 'outline' as const, speed: 3 }
    expect(resolveGroupStyle(a, defaults)).toEqual({ open: 'unfold', card: 'preview', boundary: 'outline', speed: 3 })
    expect(resolveGroupStyle(b, defaults)).toEqual({ open: 'burst', card: 'stacked', boundary: 'solid', speed: 1.5 })
  })

  it('ignores values it does not know and clamps the speed', () => {
    expect(resolveGroupStyle({ style: { open: 'explode' as never, speed: 99 } })).toEqual({ ...BUILT_IN_GROUP_STYLE, speed: 3 })
    expect(clampSpeed(0.1)).toBe(0.25)
    expect(clampSpeed(1.3)).toBe(1.25)
    expect(clampSpeed('fast')).toBe(1)
  })

  it('"Use defaults" clears a group\'s own style and leaves the others alone', () => {
    const groups = [g('a', ['1', '2'], { style: { open: 'fan' } }), g('b', ['3', '4'], { style: { open: 'burst' } })]
    const next = setGroupStyle(groups, 'a', null)
    expect(next[0].style).toBeUndefined()
    expect(next[1].style).toEqual({ open: 'burst' })
    expect(setGroupStyle(next, 'a', { card: 'stacked' })[0].style).toEqual({ card: 'stacked' })
  })
})

describe('group bookkeeping', () => {
  it('cleans a saved list: unknown nodes dropped, first claim wins, tiny groups removed, blank names named', () => {
    const out = cleanGroups([
      g('a', ['1', '2', 'ghost'], { name: '  ' }),
      g('b', ['2', '3']),
      g('a', ['4', '5']),
      g('c', ['4', '5', '4']),
    ], ['1', '2', '3', '4', '5'])
    expect(out).toEqual([
      { id: 'a', name: 'Group', nodeIds: ['1', '2'], collapsed: true },
      { id: 'c', name: 'c', nodeIds: ['4', '5'], collapsed: true },
    ])
  })

  it('a node joining a new group leaves its old one, which goes away when too small', () => {
    const next = createGroup([g('old', ['1', '2', '3'])], ['2', '3', '4'], ' Notify ', 'new')
    expect(next).toEqual([{ id: 'new', name: 'Notify', nodeIds: ['2', '3', '4'], collapsed: true }])
    expect(createGroup([], ['1'], 'x', 'y')).toEqual([])
  })

  it('a new group has no style of its own, so it follows the defaults', () => {
    expect(createGroup([], ['1', '2'], 'x', 'y')[0].style).toBeUndefined()
  })

  it('will not rename a group to nothing', () => {
    expect(renameGroup([g('a', ['1', '2'])], 'a', '  ')[0].name).toBe('a')
    expect(renameGroup([g('a', ['1', '2'])], 'a', ' Lookup ')[0].name).toBe('Lookup')
  })
})

describe('edges with collapsed groups', () => {
  const edges = [e('t-v', 't', 'v'), e('t-k', 't', 'k'), e('v-k', 'v', 'k'), e('k-m', 'k', 'm', 'true'), e('v-m', 'v', 'm'), e('m-r', 'm', 'r')]

  it('hides inner edges, attaches outside ones to the card and merges duplicates with a count', () => {
    const out = foldEdges(edges, [g('a', ['v', 'k'])])
    const card = groupCardId('a')
    expect(out.map((x) => `${x.source}->${x.target}`)).toEqual([`t->${card}`, `${card}->m`, 'm->r'])
    expect(out[0].data?.groupEdge).toEqual({ count: 2, edgeIds: ['t-v', 't-k'] })
    expect(out[1].data?.groupEdge).toEqual({ count: 2, edgeIds: ['k-m', 'v-m'] })
    expect(out[1].sourceHandle).toBeNull()
    expect(out[2]).toBe(edges[5])
  })

  it('keeps the outside end\'s port, so edges into different ports stay apart', () => {
    const out = foldEdges([e('a', 'c', 'x', 'true'), e('b', 'c', 'y', 'false')], [g('G', ['x', 'y'])])
    expect(out.map((x) => x.data?.groupEdge)).toEqual([{ count: 1, edgeIds: ['a'] }, { count: 1, edgeIds: ['b'] }])
  })

  it('joins two collapsed groups card to card, and leaves open groups alone', () => {
    const groups = [g('a', ['v', 'k']), g('b', ['m', 'r'])]
    expect(foldEdges(edges, groups).map((x) => `${x.source}->${x.target}`)).toEqual([`t->${groupCardId('a')}`, `${groupCardId('a')}->${groupCardId('b')}`])
    expect(foldEdges(edges, [g('a', ['v', 'k'], { collapsed: false })])).toEqual(edges)
  })
})

describe('geometry and flow order', () => {
  it('draws the boundary around the members with room for the label', () => {
    const nodes = [
      { id: 'a', position: { x: 100, y: 50 }, measured: { width: 100, height: 40 } },
      { id: 'b', position: { x: 300, y: 200 }, width: 120, height: 50 },
      { id: 'c', position: { x: 0, y: 0 } },
    ]
    expect(groupBounds(nodes, ['a', 'b'], 20, 10)).toEqual({ x: 80, y: 20, width: 360, height: 250 })
    expect(groupBounds(nodes, ['nope'])).toBeNull()
  })

  it('keeps the card between its minimum and maximum width', () => {
    expect(cardWidth(null)).toBe(150)
    expect(cardWidth({ x: 0, y: 0, width: 90, height: 1 })).toBe(150)
    expect(cardWidth({ x: 0, y: 0, width: 900, height: 1 })).toBe(240)
  })

  it('ranks members by their step in the flow, and survives a loop', () => {
    const rank = flowRank(['a', 'b', 'c', 'd'], [e('1', 'a', 'b'), e('2', 'b', 'c'), e('3', 'a', 'c'), e('4', 'x', 'd')])
    expect(Object.fromEntries(rank)).toEqual({ a: 0, b: 1, c: 2, d: 0 })
    const loop = flowRank(['a', 'b'], [e('1', 'a', 'b'), e('2', 'b', 'a')])
    expect(Math.max(...loop.values())).toBeLessThan(2)
  })
})
