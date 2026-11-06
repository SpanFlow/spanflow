import { create } from 'zustand'
import { api } from '../lib/api'
import { dataflowApi } from '../lib/dataflowApi'
import { docId, parseDocId, type DocSource } from '../lib/docLinks'

export interface DocSummary {
  slug: string
  title: string
  category: string
}

/** A guide page as the drawer holds it: the summary plus which guide it came from. */
export interface DocListEntry extends DocSummary {
  source: DocSource
  /** The drawer's address for this page — `slug` for DataFlow, `platform:slug` for Platform. */
  id: string
}

export interface DocPage extends DocSummary {
  markdown: string
}

interface DocsState {
  /** True while the Help drawer is open. */
  open: boolean
  /** The page id currently shown (null → the index). */
  slug: string | null
  list: DocListEntry[]
  listLoaded: boolean
  /** Fetched page bodies, cached by id. */
  pages: Record<string, DocPage>
  loadingPage: boolean
  error: string | null

  /** Opens the drawer, optionally at a specific page id. */
  openHelp: (slug?: string) => void
  closeHelp: () => void
  /** Navigates the drawer to a page id (fetches + caches the page). */
  show: (slug: string) => Promise<void>
  fetchList: () => Promise<void>
}

const CLIENTS: Record<DocSource, typeof api> = { dataflow: dataflowApi, platform: api }
const SOURCE_LABELS: Record<DocSource, string> = { dataflow: 'DataFlow', platform: 'Platform' }

/**
 * The in-app user guide (E08-T16, second source added by E23-T10). Both services serve their own
 * `/api/docs` from an embedded `docs/guide/`, so the repo docs and the drawer share one source in
 * each. The lists are merged and fetched independently: one service being down costs its half of
 * the guide, not the drawer.
 */
export const useDocsStore = create<DocsState>((set, get) => ({
  open: false,
  slug: null,
  list: [],
  listLoaded: false,
  pages: {},
  loadingPage: false,
  error: null,

  openHelp: (slug) => {
    set({ open: true })
    get().fetchList()
    if (slug) get().show(slug)
    else set({ slug: null })
  },

  closeHelp: () => set({ open: false }),

  fetchList: async () => {
    if (get().listLoaded) return

    const sources: DocSource[] = ['dataflow', 'platform']
    const failed: DocSource[] = []

    // Each source lands on its own: a slow or dead service delays its half of the nav, never the
    // other's. Awaiting both together would make the whole guide as slow as the slowest API.
    await Promise.all(
      sources.map(async (source) => {
        try {
          const { data } = await CLIENTS[source].get<DocSummary[]>('/api/docs')
          const entries = data.map((d) => ({ ...d, source, id: docId(source, d.slug) }))
          set((s) => ({ list: [...s.list.filter((d) => d.source !== source), ...entries] }))
        } catch {
          failed.push(source)
        }
      }),
    )

    // A partial guide is still worth keeping, so the list stands and the error names the service
    // that is missing rather than implying the whole drawer failed. Only a total outage is worth
    // retrying on the next open.
    set({
      listLoaded: failed.length < sources.length,
      error:
        failed.length === 0
          ? null
          : failed.length === sources.length
            ? "Couldn't load the docs: none of the services answered. They may be stopped or restarting: try again in a few seconds."
            : `Some guides are unavailable: the ${failed.map((f) => SOURCE_LABELS[f]).join(' and ')} API didn't answer (it may be stopped or restarting).`,
    })
  },

  show: async (slug) => {
    set({ slug })
    if (get().pages[slug]) return
    set({ loadingPage: true })
    const { source, slug: path } = parseDocId(slug)
    try {
      const { data } = await CLIENTS[source].get<DocPage>(`/api/docs/${path}`)
      set((s) => ({ pages: { ...s.pages, [slug]: data }, loadingPage: false, error: null }))
    } catch {
      set({ loadingPage: false, error: `Could not load "${path}".` })
    }
  },
}))
