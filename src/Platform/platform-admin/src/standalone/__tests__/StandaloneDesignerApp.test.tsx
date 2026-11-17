import { describe, it, expect, beforeEach } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import 'fake-indexeddb/auto'
import { IDBFactory } from 'fake-indexeddb'
import StandaloneDesignerApp, { STANDALONE_WORKSPACE_KEY } from '../StandaloneDesignerApp'
import { useStore } from '../../mapping/store/useStore'
import { saveWorkspace } from '../../mapping/store/templateDb'

/**
 * The standalone designer page: the shared designer with no sign-in and no projects. It restores its one
 * browser workspace, and offers nothing that needs the Studio (the saved-mappings library is a project's).
 */

beforeEach(() => {
  globalThis.indexedDB = new IDBFactory()
  localStorage.setItem('sf-consent', 'all')
  useStore.getState().resetWorkspace()
})

describe('StandaloneDesignerApp', () => {
  it('restores its own browser workspace', async () => {
    await saveWorkspace(
      {
        docs: [{ id: 'main', kind: 'mapping', name: 'Saved here' }],
        activeDocId: 'main',
        graphs: { main: { nodes: [], edges: [], sampleInput: '{}' } },
      },
      STANDALONE_WORKSPACE_KEY,
    )

    render(<StandaloneDesignerApp />)

    await waitFor(() => expect(useStore.getState().docs.map((d) => d.name)).toEqual(['Saved here']))
  })

  it('has the designer tools but no saved-mappings library', () => {
    render(<StandaloneDesignerApp />)

    expect(screen.getByRole('button', { name: /Examples/ })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Save Pipeline/ })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^Mappings$/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Maximize' })).not.toBeInTheDocument()
    expect(screen.getByText('Standalone')).toBeInTheDocument()
  })

  it('links "Powered by" to the product website', () => {
    render(<StandaloneDesignerApp />)

    const link = screen.getByRole('link', { name: /Powered by/ })
    expect(link.getAttribute('href')).toMatch(/^https:\/\/spanflow\.app\/\?utm_source=mapping-playground/)
    expect(link).toHaveAttribute('target', '_blank')
  })

  it('asks a first-time visitor about cookies, remembers the answer, and opens again from Cookie settings', () => {
    localStorage.removeItem('sf-consent')
    const { unmount } = render(<StandaloneDesignerApp />)

    const notice = screen.getByRole('dialog', { name: 'Cookies and site storage' })
    fireEvent.click(within(notice).getByRole('button', { name: 'Essential only' }))
    expect(screen.queryByRole('dialog', { name: 'Cookies and site storage' })).not.toBeInTheDocument()
    expect(localStorage.getItem('sf-consent')).toBe('essential')

    unmount()
    render(<StandaloneDesignerApp />)
    expect(screen.queryByRole('dialog', { name: 'Cookies and site storage' })).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Cookie settings' }))
    fireEvent.click(screen.getByRole('button', { name: 'Accept all' }))
    expect(localStorage.getItem('sf-consent')).toBe('all')
  })
})
