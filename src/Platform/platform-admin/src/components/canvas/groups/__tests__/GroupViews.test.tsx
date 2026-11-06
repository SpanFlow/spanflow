import { describe, expect, it, vi, afterEach } from 'vitest'
import { act, render, renderHook, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import GroupCard from '../GroupCard'
import GroupBoundary from '../GroupBoundary'
import { useNodeGroups } from '../useNodeGroups'
import { CARD_STYLES, type NodeGroup } from '../nodeGroups'

const LONG = 'Customer lookup and enrichment for the loyalty programme v2'

describe('GroupCard', () => {
  it.each(CARD_STYLES)('%s keeps a long name inside the card: two lines, an ellipsis, the full name in the tooltip', (look) => {
    render(<GroupCard name={LONG} memberCount={4} look={look} onOpen={() => {}} />)
    const card = screen.getByTestId('group-card')
    // jsdom does no layout, so this asserts the classes that make the browser keep it in (D61.6).
    expect(screen.getByTestId('group-card-name').className).toMatch(/line-clamp-2/)
    expect(card.querySelector('.overflow-hidden')).not.toBeNull()
    expect(card.querySelector('.min-w-0')).not.toBeNull()
    expect(card.getAttribute('title')).toContain(LONG)
    expect(card).toHaveAttribute('data-look', look)
  })

  it('shows the mini preview only in the preview look, and the stack only in the stacked look', () => {
    const { rerender, container } = render(<GroupCard name="A" memberCount={3} look="preview" onOpen={() => {}} />)
    expect(screen.getByTestId('group-card-preview')).toBeInTheDocument()
    rerender(<GroupCard name="A" memberCount={3} look="stacked" onOpen={() => {}} />)
    expect(screen.queryByTestId('group-card-preview')).toBeNull()
    expect(container.querySelectorAll('[aria-hidden="true"].absolute')).toHaveLength(2)
  })

  it('opens on click and says how a run went', async () => {
    const onOpen = vi.fn()
    render(<GroupCard name="Notify" memberCount={2} look="count" runState={{ running: 0, failed: 1, succeeded: 1 }} onOpen={onOpen} />)
    expect(screen.getByText(/1 failed/)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: /Open group Notify, 2 nodes, 1 failed/ }))
    expect(onOpen).toHaveBeenCalledOnce()
  })
})

describe('GroupBoundary', () => {
  it('closes from its label', async () => {
    const onClose = vi.fn()
    render(<GroupBoundary name="Notify" memberCount={2} look="solid" width={200} height={100} onClose={onClose} />)
    expect(screen.getByTestId('group-boundary')).toHaveAttribute('data-look', 'solid')
    await userEvent.click(screen.getByRole('button', { name: 'Close group Notify' }))
    expect(onClose).toHaveBeenCalledOnce()
  })
})

describe('useNodeGroups', () => {
  afterEach(() => vi.useRealTimers())

  const nodes = [
    { id: 't', position: { x: 0, y: 0 } },
    { id: 'v', position: { x: 200, y: 0 } },
    { id: 'k', position: { x: 200, y: 200 } },
  ]
  const edges = [{ id: '1', source: 't', target: 'v' }, { id: '2', source: 't', target: 'k' }]

  it('folds edges and hides members of a collapsed group', () => {
    const groups: NodeGroup[] = [{ id: 'g', name: 'G', nodeIds: ['v', 'k'], collapsed: true }]
    const { result } = renderHook(() => useNodeGroups({ groups, onChange: () => {}, nodes, edges }))
    expect([...result.current.hiddenNodeIds]).toEqual(['v', 'k'])
    expect(result.current.visibleEdges).toHaveLength(1)
    expect(result.current.layout[0].style.open).toBe('spiral')
  })

  it('opens at once and animates; closing marks it collapsed only when the animation ends', () => {
    vi.useFakeTimers()
    const onChange = vi.fn()
    const closed: NodeGroup[] = [{ id: 'g', name: 'G', nodeIds: ['v', 'k'], collapsed: true }]
    const { result, rerender } = renderHook(({ groups }) => useNodeGroups({ groups, onChange, nodes, edges }), { initialProps: { groups: closed } })

    act(() => result.current.open('g'))
    expect(onChange).toHaveBeenLastCalledWith([{ ...closed[0], collapsed: false }])
    expect(result.current.frame('g', 'node:v')).toBeDefined()

    const opened = [{ ...closed[0], collapsed: false }]
    rerender({ groups: opened })
    act(() => { vi.advanceTimersByTime(3000) })
    expect(result.current.isPlaying('g')).toBe(false)

    onChange.mockClear()
    act(() => result.current.close('g'))
    expect(onChange).not.toHaveBeenCalled()
    act(() => { vi.advanceTimersByTime(3000) })
    expect(onChange).toHaveBeenCalledWith([{ ...closed[0], collapsed: true }])
  })

  it('groups the selection under a name', () => {
    const onChange = vi.fn()
    const { result } = renderHook(() => useNodeGroups({ groups: [], onChange, nodes, edges, newId: () => 'new' }))
    act(() => { result.current.group(['v', 'k'], 'Lookup') })
    expect(onChange).toHaveBeenCalledWith([{ id: 'new', name: 'Lookup', nodeIds: ['v', 'k'], collapsed: true }])
  })
})
