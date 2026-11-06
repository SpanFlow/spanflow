import { MappingDesigner } from '../mapping/MappingDesigner'
import ThemeButton from '../components/ThemeButton'
import { mappingName } from '../lib/brand'

/** The one workspace this page autosaves, in the browser's own storage (no account, no server). */
export const STANDALONE_WORKSPACE_KEY = 'standalone'

/**
 * The standalone mapping designer: the Studio's designer on a page of its own, served by the designer API
 * in its standalone mode. No sign-in and no projects, so there is no saved-mappings library either: a
 * mapping lives in this browser's workspace and in the files Save Pipeline writes.
 */
export default function StandaloneDesignerApp() {
  return (
    <MappingDesigner
      layout="page"
      workspaceKey={STANDALONE_WORKSPACE_KEY}
      title={
        <div className="flex items-center gap-2">
          <img src="/favicon.svg" alt="" className="h-5 w-5" />
          <span className="text-sm font-semibold text-slate-700 dark:text-slate-200">{mappingName}</span>
          <span
            className="text-[11px] font-medium text-slate-500 dark:text-slate-400 px-2 py-0.5 rounded-full border border-slate-200 dark:border-slate-700"
            title="Running on its own: no sign-in. Anyone who can open this page can run mappings with it."
          >
            Standalone
          </span>
          <ThemeButton className="p-1.5 rounded-full hover:bg-slate-100 dark:hover:bg-slate-800 text-slate-500 dark:text-slate-400 transition-colors flex items-center gap-1" />
        </div>
      }
    />
  )
}
