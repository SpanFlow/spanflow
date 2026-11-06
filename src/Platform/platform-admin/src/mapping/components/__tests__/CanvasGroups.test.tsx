import { describe, it, expect, beforeEach, vi } from 'vitest'
import { render, screen, fireEvent, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { Node } from '@xyflow/react'
import { Canvas } from '../Canvas'
import { useStore } from '../../store/useStore'

// E61-T5 on the mapping canvas. Clicks on the canvas itself are fireEvent: userEvent's mousedown
// reaches d3-drag, which needs a window jsdom does not give the event.
vi.setConfig({ testTimeout: 15_000 })

const node = (id: string, type: string, x: number, label: string): Node =>
  ({ id, type, position: { x, y: 0 }, data: { label, fields: ['id'] } }) as Node

const nodeOnCanvas = (id: string) => document.querySelector(`.react-flow__node[data-id="${id}"]`)

function seed(collapsed: boolean) {
  useStore.setState({
    nodes: [node('src', 'source', 0, 'Input'), node('a', 'source', 300, 'Extra A'), node('b', 'source', 600, 'Extra B')],
    edges: [],
    groups: [{ id: 'g1', name: 'Extras', nodeIds: ['a', 'b'], collapsed }],
    past: [],
    future: [],
    _lastPush: 0,
    debug: { active: false, running: false, results: {}, inspectNodeId: null },
  })
}

describe('mapping canvas node groups (E61-T5)', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('shows a folded group as one card where its members were, and hides them', () => {
    seed(true)
    render(<Canvas />)
    expect(screen.getByLabelText('Open group Extras, 2 nodes')).toBeInTheDocument()
    expect(nodeOnCanvas('a')).toBeNull()
    expect(nodeOnCanvas('src')).not.toBeNull()
  })

  it('opens the card into a boundary with every member back where it was', async () => {
    seed(true)
    render(<Canvas />)
    fireEvent.click(screen.getByLabelText(/Open group Extras/))

    expect(await screen.findByLabelText('Close group Extras')).toBeInTheDocument()
    expect(useStore.getState().groups[0].collapsed).toBe(false)
    // Free positions: opening a group never moves anything.
    expect(useStore.getState().nodes.map((n) => n.position.x)).toEqual([0, 300, 600])
    // And it is an edit that can be undone.
    useStore.getState().undo()
    expect(useStore.getState().groups[0].collapsed).toBe(true)
  })

  it('shows a debug run failure inside a folded group on its card', () => {
    seed(true)
    useStore.setState({
      debug: {
        active: true,
        running: false,
        inspectNodeId: null,
        results: { a: { value: 1, elapsedMs: 1 }, b: { value: undefined, elapsedMs: 1, error: 'boom' } },
      },
    })
    render(<Canvas />)
    expect(screen.getByLabelText('Open group Extras, 2 nodes, 1 failed')).toBeInTheDocument()
  })

  it('ungroups from the right-click menu', async () => {
    seed(true)
    render(<Canvas />)
    fireEvent.contextMenu(nodeOnCanvas('group:g1')!)
    await userEvent.click(within(screen.getByRole('menu', { name: 'Group actions' })).getByRole('menuitem', { name: 'Ungroup' }))
    expect(useStore.getState().groups).toEqual([])
    expect(nodeOnCanvas('a')).not.toBeNull()
  })

  it('groups the selected nodes under the name asked for, with Ctrl+G', async () => {
    vi.spyOn(window, 'prompt').mockReturnValue('Pair')
    seed(false)
    useStore.setState({ groups: [] })
    render(<Canvas />)
    expect(screen.getByRole('button', { name: 'Group' })).toBeDisabled()

    const user = userEvent.setup()
    fireEvent.click(nodeOnCanvas('src')!)
    await user.keyboard('{Control>}')
    fireEvent.click(nodeOnCanvas('a')!)
    await user.keyboard('{/Control}')
    fireEvent.keyDown(nodeOnCanvas('a')!, { key: 'g', ctrlKey: true })

    expect(await screen.findByLabelText('Open group Pair, 2 nodes')).toBeInTheDocument()
    expect(useStore.getState().groups).toMatchObject([{ name: 'Pair', nodeIds: ['src', 'a'], collapsed: true }])
  })
})
