import { useEffect, useState } from 'react'
import ReactMarkdown, { type Components } from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { X, BookOpen, List, ChevronLeft } from 'lucide-react'
import { useDocsStore } from '../stores/docsStore'
import { resolveDocSlug, type DocSource } from '../lib/docLinks'
import ErrorBanner from './ErrorBanner'

const CATEGORY_LABELS: Record<string, string> = {
  index: 'Overview',
  concepts: 'Guides',
  reference: 'Reference',
  admin: 'Admin surfaces',
}
const CATEGORY_ORDER = ['index', 'concepts', 'reference', 'admin']

/** Which guide a group of pages came from. Rendered as a heading only when both are present. */
const SOURCE_ORDER: DocSource[] = ['dataflow', 'platform']
const SOURCE_LABELS: Record<DocSource, string> = { dataflow: 'Pipelines', platform: 'Administration' }

/**
 * In-app user guide (E08-T16; second source added by E23-T10). A right-side drawer that lists the
 * guide pages from both services — DataFlow's pipeline guide and Platform's admin guide — and
 * renders the selected one; internal `.md` cross-links navigate within the drawer (staying in the
 * guide they were written in), external links open in a new tab. Opened globally from the header and
 * from contextual "Learn more" links.
 */
export default function HelpDrawer() {
  const { open, slug, list, pages, loadingPage, error, closeHelp, show } = useDocsStore()
  const activeSlug = slug ?? 'README'
  const page = pages[activeSlug]
  // Below md the guide list and the page can't both have the screen, so the list is a panel you
  // open over the page (like the section rail) rather than a permanent 12rem column squeezing the
  // reading width to nothing. Picking a page closes it. Above md both show side by side as before.
  const [listOpen, setListOpen] = useState(false)
  const activeTitle = list.find((d) => d.id === activeSlug)?.title

  useEffect(() => {
    if (open) show(activeSlug)
  }, [open, activeSlug, show])

  if (!open) return null

  const sourcesPresent = new Set(list.map((d) => d.source)).size

  const markdownComponents: Components = {
    a: ({ href, children }) => {
      const target = href ? resolveDocSlug(activeSlug, href) : null
      if (target !== null) {
        return (
          <button type="button" onClick={() => show(target)} className="text-primary hover:underline">
            {children}
          </button>
        )
      }
      return <a href={href} target="_blank" rel="noreferrer" className="text-primary hover:underline">{children}</a>
    },
    h1: ({ children }) => <h1 className="mb-3 mt-1 text-lg font-semibold text-slate-800 dark:text-slate-100">{children}</h1>,
    h2: ({ children }) => <h2 className="mb-2 mt-5 border-b border-slate-200 pb-1 text-base font-semibold text-slate-700 dark:border-slate-800 dark:text-slate-200">{children}</h2>,
    h3: ({ children }) => <h3 className="mb-1 mt-4 text-sm font-semibold text-slate-700 dark:text-slate-200">{children}</h3>,
    p: ({ children }) => <p className="my-2 text-sm leading-relaxed text-slate-600 dark:text-slate-300">{children}</p>,
    ul: ({ children }) => <ul className="my-2 list-disc space-y-1 pl-5 text-sm text-slate-600 dark:text-slate-300">{children}</ul>,
    ol: ({ children }) => <ol className="my-2 list-decimal space-y-1 pl-5 text-sm text-slate-600 dark:text-slate-300">{children}</ol>,
    blockquote: ({ children }) => <blockquote className="my-2 border-l-2 border-sky-300 bg-sky-50 px-3 py-1 text-sm text-sky-900 dark:border-sky-800 dark:bg-sky-950/40 dark:text-sky-200">{children}</blockquote>,
    code: ({ children, className }) =>
      className ? (
        <code className={className}>{children}</code>
      ) : (
        <code className="rounded bg-slate-100 px-1 py-0.5 font-mono text-[0.8em] text-slate-700 dark:bg-slate-800 dark:text-slate-200">{children}</code>
      ),
    pre: ({ children }) => <pre className="my-2 overflow-x-auto rounded bg-slate-100 p-2 text-xs dark:bg-slate-800">{children}</pre>,
    table: ({ children }) => <div className="my-2 overflow-x-auto"><table className="w-full border-collapse text-xs">{children}</table></div>,
    th: ({ children }) => <th className="border border-slate-200 bg-slate-50 px-2 py-1 text-left font-semibold dark:border-slate-700 dark:bg-slate-800">{children}</th>,
    td: ({ children }) => <td className="border border-slate-200 px-2 py-1 align-top dark:border-slate-700">{children}</td>,
  }

  return (
    <div className="fixed inset-0 z-50 flex justify-end bg-black/30" onClick={closeHelp}>
      <aside
        className="flex h-full w-full max-w-2xl flex-col bg-white shadow-xl dark:bg-slate-900"
        onClick={(e) => e.stopPropagation()}
      >
        <header className="flex items-center gap-2 border-b border-slate-200 px-4 py-3 dark:border-slate-800">
          <BookOpen size={16} className="text-primary" />
          <h2 className="flex-1 text-sm font-semibold text-slate-700 dark:text-slate-200">Help &amp; docs</h2>
          <button onClick={closeHelp} aria-label="Close help" className="text-slate-400 hover:text-slate-600"><X size={18} /></button>
        </header>

        {/* Mobile-only switch between the guide list and the page it names — the two share the screen
            here instead of splitting it. */}
        <button
          onClick={() => setListOpen((v) => !v)}
          aria-expanded={listOpen}
          className="flex items-center gap-2 border-b border-slate-200 px-4 py-2 text-xs md:hidden dark:border-slate-800"
        >
          {listOpen ? <ChevronLeft size={14} /> : <List size={14} />}
          <span className="font-medium text-slate-600 dark:text-slate-300">
            {listOpen ? 'Back to page' : 'All guides'}
          </span>
          {!listOpen && activeTitle && <span className="truncate text-slate-400">· {activeTitle}</span>}
        </button>

        <div className="flex min-h-0 flex-1">
          <nav className={`w-full shrink-0 overflow-y-auto border-slate-200 p-2 md:block md:w-48 md:border-r dark:border-slate-800 ${listOpen ? 'block' : 'hidden'}`}>
            {SOURCE_ORDER.map((source) => {
              const fromSource = list.filter((d) => d.source === source)
              if (fromSource.length === 0) return null
              return (
                <div key={source} className="mb-4">
                  {sourcesPresent > 1 && (
                    <div className="mb-1 border-b border-slate-200 px-1 pb-1 text-[11px] font-semibold text-slate-500 dark:border-slate-800 dark:text-slate-400">
                      {SOURCE_LABELS[source]}
                    </div>
                  )}
                  {CATEGORY_ORDER.map((category) => {
                    const items = fromSource.filter((d) => d.category === category)
                    if (items.length === 0) return null
                    return (
                      <div key={category} className="mb-3">
                        <div className="mb-1 px-1 text-[10px] font-semibold uppercase tracking-wide text-slate-400">{CATEGORY_LABELS[category] ?? category}</div>
                        {items.map((item) => (
                          <button
                            key={item.id}
                            onClick={() => {
                              show(item.id)
                              setListOpen(false)
                            }}
                            className={`block w-full truncate rounded px-2 py-1 text-left text-xs ${
                              item.id === activeSlug
                                ? 'bg-primary/10 font-medium text-primary'
                                : 'text-slate-600 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-slate-800'
                            }`}
                          >
                            {item.title}
                          </button>
                        ))}
                      </div>
                    )
                  })}
                </div>
              )
            })}
          </nav>

          {/* Labelled as its own region so the page's own links are distinguishable from the nav's:
              both guides have a "Logs" and a screen reader (or a test) needs to know which it is on. */}
          <div role="region" aria-label="Guide page" className={`min-w-0 flex-1 overflow-y-auto p-4 md:block ${listOpen ? 'hidden' : 'block'}`}>
            <ErrorBanner message={error} />
            {loadingPage && !page ? (
              <p className="text-sm text-slate-400">Loading…</p>
            ) : page ? (
              <ReactMarkdown remarkPlugins={[remarkGfm]} components={markdownComponents}>{page.markdown}</ReactMarkdown>
            ) : (
              !error && <p className="text-sm text-slate-400">Select a guide.</p>
            )}
          </div>
        </div>
      </aside>
    </div>
  )
}

/** A small inline "Learn more" link that opens the Help drawer at a guide slug. */
export function LearnMore({ slug, label = 'Learn more' }: { slug: string; label?: string }) {
  const openHelp = useDocsStore((s) => s.openHelp)
  return (
    <button type="button" onClick={() => openHelp(slug)} className="inline-flex items-center gap-0.5 text-[11px] text-primary hover:underline">
      <BookOpen size={10} /> {label}
    </button>
  )
}
