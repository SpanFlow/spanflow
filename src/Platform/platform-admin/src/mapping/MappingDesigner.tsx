import { useEffect, useRef, useState, type ReactNode } from 'react'
import { Sidebar } from './components/Sidebar'
import { Canvas } from './components/Canvas'
import { PreviewPanel } from './components/PreviewPanel'
import { ExamplesPanel } from './components/ExamplesPanel'
import { HistoryPanel } from './components/HistoryPanel'
import { DebugModal } from './components/DebugModal'
import { DocumentTabs } from './components/DocumentTabs'
import { useStore } from './store/useStore'
import type { WorkspaceSnapshot } from './store/useStore'
import { loadWorkspace, saveWorkspace } from './store/templateDb'
import { savePipeline, loadPipelineFromText } from './store/pipelineIO'
import { Download, Upload, Undo2, Redo2, FileDown, FileUp, BookOpen, Clock, Maximize2, Minimize2 } from 'lucide-react'
import { useMaximized } from '../lib/useMaximized'

export interface MappingDesignerProps {
  /** Whose autosaved workspace this is: a project id in the admin app, a fixed key in the standalone page. */
  workspaceKey: string | undefined
  /** Called once this key's workspace has been restored (anything opened earlier is wiped by the restore). */
  onRestored?: (workspaceKey: string | undefined) => void
  /** Left of the toolbar. */
  title: ReactNode
  /** Toolbar buttons after Examples, e.g. the admin app's saved-mappings library. */
  toolbarExtras?: ReactNode
  /** Dialogs the host opens from its own toolbar buttons. */
  children?: ReactNode
  /**
   * `embedded`: a panel inside a page, with a maximize toggle (the admin app's project tab).
   * `page`: the whole viewport (the standalone designer), so there is nothing to maximize.
   */
  layout?: 'embedded' | 'page'
}

const pill =
  'text-xs font-semibold text-slate-600 dark:text-slate-300 px-2.5 py-1.5 rounded-full border border-slate-200 dark:border-slate-700 hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors flex items-center gap-1'
const iconButton =
  'p-1.5 rounded-full hover:bg-slate-100 dark:hover:bg-slate-800 text-slate-500 dark:text-slate-400 transition-colors disabled:opacity-30 disabled:hover:bg-transparent'

/**
 * The mapping designer: toolbar, node sidebar, canvas, preview and the dialogs they open, with the
 * workspace autosaved in the browser. Shared by the admin app's project tab (components/MappingPanel)
 * and the standalone designer page (standalone/), which differ only in what they add around it.
 */
export function MappingDesigner({ workspaceKey, onRestored, title, toolbarExtras, children, layout = 'embedded' }: MappingDesignerProps) {
  const [showExamples, setShowExamples] = useState(false)
  const [showHistory, setShowHistory] = useState(false)
  const { maximized, toggle: toggleMaximized } = useMaximized()
  const restored = useRef(false)
  const onRestoredRef = useRef(onRestored)
  onRestoredRef.current = onRestored

  // Restore this key's saved workspace, then autosave (debounced) on every change.
  //
  // The workspace is per-project (E16-T5): the designer store is a module singleton and survives
  // the route change, so switching projects resets it *before* hydrating — otherwise a project
  // with nothing saved would silently keep showing the previous project's tabs, and the autosave
  // below would then write them into its record.
  useEffect(() => {
    restored.current = false
    useStore.getState().resetWorkspace()

    let stale = false
    loadWorkspace<WorkspaceSnapshot>(workspaceKey)
      .then((ws) => {
        if (stale) return // the key changed again while this read was in flight
        if (ws) useStore.getState().hydrateWorkspace(ws)
      })
      .catch(() => {})
      .finally(() => {
        if (!stale) {
          restored.current = true
          onRestoredRef.current?.(workspaceKey)
        }
      })

    return () => {
      stale = true
      restored.current = false
    }
  }, [workspaceKey])

  useEffect(() => {
    let t: ReturnType<typeof setTimeout>
    const unsub = useStore.subscribe(() => {
      if (!restored.current) return
      clearTimeout(t)
      t = setTimeout(() => {
        saveWorkspace(useStore.getState().serializeWorkspace(), workspaceKey).catch(() => {})
      }, 800)
    })
    return () => {
      clearTimeout(t)
      unsub()
    }
  }, [workspaceKey])

  const undo = useStore((s) => s.undo)
  const redo = useStore((s) => s.redo)
  const canUndo = useStore((s) => s.past.length > 0)
  const canRedo = useStore((s) => s.future.length > 0)
  const importSource = useStore((s) => s.importSource)
  const importTarget = useStore((s) => s.importTarget)
  const fileInput = useRef<HTMLInputElement>(null)
  const sourceInput = useRef<HTMLInputElement>(null)
  const targetInput = useRef<HTMLInputElement>(null)

  const onLoadFile = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    if (!file) return
    try {
      loadPipelineFromText(await file.text())
    } catch (err: any) {
      alert(`Could not load pipeline: ${err.message}`)
    }
    e.target.value = '' // allow re-loading the same file
  }

  const onImport = (apply: (text: string) => void) => async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    if (!file) return
    try {
      apply(await file.text())
    } catch (err: any) {
      alert(`Could not import sample: ${err.message}`)
    }
    e.target.value = ''
  }

  // Ctrl/Cmd+Z = undo, Ctrl/Cmd+Shift+Z or Ctrl+Y = redo. Skip while typing in a field.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (!(e.ctrlKey || e.metaKey)) return
      const el = e.target as HTMLElement | null
      const tag = el?.tagName
      if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || el?.isContentEditable) return
      const key = e.key.toLowerCase()
      if (key === 'z' && !e.shiftKey) {
        e.preventDefault()
        useStore.getState().undo()
      } else if ((key === 'z' && e.shiftKey) || key === 'y') {
        e.preventDefault()
        useStore.getState().redo()
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

  const page = layout === 'page'
  const frame = page
    ? 'flex flex-col h-screen bg-white dark:bg-slate-900 overflow-hidden'
    : maximized
      ? 'fixed inset-0 z-50 flex flex-col bg-white dark:bg-slate-900 overflow-hidden'
      : 'flex flex-col rounded border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 overflow-hidden'
  const workspace = page || maximized ? 'flex-1 min-h-0' : 'h-[calc(100vh-16rem)] min-h-[480px]'

  return (
    <div className={frame}>
      <div className="flex items-center justify-between gap-2 px-3 py-2 border-b border-slate-200 dark:border-slate-800 flex-wrap">
        {title}
        <div className="flex items-center gap-2 flex-wrap">
          <button onClick={() => setShowExamples(true)} className={pill} title="Browse worked examples and load them into the canvas">
            <BookOpen size={13} /> Examples
          </button>
          {toolbarExtras}
          <button onClick={() => setShowHistory(true)} className={pill} title="Save and reload scenarios stored in your browser">
            <Clock size={13} /> History
          </button>
          <div className="flex items-center gap-1">
            <button onClick={undo} disabled={!canUndo} className={iconButton} title="Undo (Ctrl+Z)">
              <Undo2 size={16} />
            </button>
            <button onClick={redo} disabled={!canRedo} className={iconButton} title="Redo (Ctrl+Shift+Z)">
              <Redo2 size={16} />
            </button>
          </div>
          <div className="flex items-center gap-1">
            <input ref={sourceInput} type="file" accept=".json,application/json" className="hidden" onChange={onImport(importSource)} />
            <button
              onClick={() => sourceInput.current?.click()}
              className="text-xs font-semibold text-indigo-600 dark:text-indigo-400 px-2.5 py-1.5 rounded-full border border-indigo-200 dark:border-indigo-500/30 hover:bg-indigo-50 dark:hover:bg-indigo-500/10 transition-colors flex items-center gap-1"
              title="Import a source sample JSON to build the Source fields (and sample input)"
            >
              <FileDown size={13} /> Source
            </button>
            <input ref={targetInput} type="file" accept=".json,application/json" className="hidden" onChange={onImport(importTarget)} />
            <button
              onClick={() => targetInput.current?.click()}
              className="text-xs font-semibold text-purple-600 dark:text-purple-400 px-2.5 py-1.5 rounded-full border border-purple-200 dark:border-purple-500/30 hover:bg-purple-50 dark:hover:bg-purple-500/10 transition-colors flex items-center gap-1"
              title="Import a target sample JSON to build the Target fields"
            >
              <FileUp size={13} /> Target
            </button>
          </div>
          <input ref={fileInput} type="file" accept=".json,application/json" className="hidden" onChange={onLoadFile} />
          <button onClick={() => fileInput.current?.click()} className={pill} title="Load a saved pipeline or a mapping .json">
            <Upload size={13} /> Load
          </button>
          <button
            onClick={savePipeline}
            className="text-xs font-semibold bg-indigo-50 dark:bg-indigo-500/10 text-indigo-600 dark:text-indigo-400 px-3 py-1.5 rounded-full border border-indigo-200 dark:border-indigo-500/30 hover:bg-indigo-100 dark:hover:bg-indigo-500/20 transition-colors flex items-center gap-1"
            title="Save this pipeline to a file"
          >
            <Download size={13} /> Save Pipeline
          </button>
          {!page && (
            <button
              onClick={toggleMaximized}
              className={iconButton}
              title={maximized ? 'Restore (Esc)' : 'Maximize (fit to screen)'}
              aria-label={maximized ? 'Restore' : 'Maximize'}
            >
              {maximized ? <Minimize2 size={16} /> : <Maximize2 size={16} />}
            </button>
          )}
        </div>
      </div>

      <div className={`flex w-full overflow-hidden ${workspace}`}>
        <Sidebar />
        <div className="flex-1 flex flex-col min-w-0">
          <DocumentTabs />
          <Canvas />
        </div>
        <PreviewPanel />
      </div>

      {showExamples && <ExamplesPanel onClose={() => setShowExamples(false)} />}
      {showHistory && <HistoryPanel onClose={() => setShowHistory(false)} />}
      {children}
      <DebugModal />
    </div>
  )
}
