import { describe, it, expect, vi } from 'vitest'
import { renderHook, act } from '@testing-library/react'
import { ReactFlowProvider, type Node } from '@xyflow/react'
import type { ReactNode } from 'react'
import { useCanvasGroups } from '../useCanvasGroups'
import type { NodeGroup } from '../nodeGroups'

// The layer every canvas shares (E61-T3 to T5). What is tested here holds for the pipeline, chat flow
// and mapping canvases alike; each canvas's own tests cover what it adds (storage, layout, run state).

const node = (id: string, x: number, y: number): Node => ({ id, type: 'x', position: { x, y }, data: {} })
const wrapper = ({ children }: { children: ReactNode }) => <ReactFlowProvider>{children}</ReactFlowProvider>

function setup(groups: NodeGroup[]) {
  let nodes = [node('a', 0, 0), node('b', 200, 0), node('c', 400, 0)]
  const setNodes = vi.fn((update: (n: Node[]) => Node[]) => {
    nodes = update(nodes)
  })
  const onNodesChange = vi.fn()
  const hook = renderHook(
    () =>
      useCanvasGroups({
        enabled: true,
        groups,
        onGroupsChange: vi.fn(),
        relayout: vi.fn(),
        nodes,
        edges: [
          { id: 'e1', source: 'a', target: 'b' },
          { id: 'e2', source: 'b', target: 'c' },
        ],
        setNodes,
        onNodesChange,
        onSelectNode: vi.fn(),
      }),
    { wrapper },
  )
  return { hook, setNodes, onNodesChange, nodes: () => nodes }
}

describe('useCanvasGroups', () => {
  it('drags an open group’s members when its boundary is dragged, and keeps the rest still', () => {
    const { hook, onNodesChange, nodes } = setup([{ id: 'g1', name: 'G', nodeIds: ['a', 'b'], collapsed: false }])
    const frame = hook.result.current.flowProps.nodes.find((n) => n.id === 'group-frame:g1')!

    act(() =>
      hook.result.current.flowProps.onNodesChange([
        { type: 'position', id: frame.id, position: { x: frame.position.x + 30, y: frame.position.y + 50 }, dragging: true },
      ]),
    )

    expect(nodes().map((n) => [n.id, n.position])).toEqual([
      ['a', { x: 30, y: 50 }],
      ['b', { x: 230, y: 50 }],
      ['c', { x: 400, y: 0 }],
    ])
    // A frame's own changes never reach the canvas's node state.
    expect(onNodesChange).not.toHaveBeenCalled()
  })

  it('drags a folded group’s members when its card is dragged', () => {
    const { hook, nodes } = setup([{ id: 'g1', name: 'G', nodeIds: ['b', 'c'], collapsed: true }])
    const card = hook.result.current.flowProps.nodes.find((n) => n.id === 'group:g1')!

    act(() =>
      hook.result.current.flowProps.onNodesChange([
        { type: 'position', id: card.id, position: { x: card.position.x - 10, y: card.position.y }, dragging: true },
      ]),
    )

    expect(nodes().find((n) => n.id === 'b')!.position).toEqual({ x: 190, y: 0 })
    expect(nodes().find((n) => n.id === 'c')!.position).toEqual({ x: 390, y: 0 })
  })

  it('passes a real node’s changes through untouched', () => {
    const { hook, onNodesChange, setNodes } = setup([])
    const change = { type: 'position' as const, id: 'a', position: { x: 5, y: 5 }, dragging: true }
    act(() => hook.result.current.flowProps.onNodesChange([change]))
    expect(onNodesChange).toHaveBeenCalledWith([change])
    expect(setNodes).not.toHaveBeenCalled()
  })
})
