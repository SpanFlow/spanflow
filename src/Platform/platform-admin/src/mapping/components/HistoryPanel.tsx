import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { X, Save, Trash2, Download, Clock } from 'lucide-react';
import { useStore } from '../store/useStore';
import { RESIZABLE_MODAL } from './DetailBlock';
import { saveTemplate, listTemplates, deleteTemplate, type Template } from '../store/templateDb';
import type { AppNode } from '../store/useStore';

const when = (t: number) => new Date(t).toLocaleString();

export const HistoryPanel = ({ onClose }: { onClose: () => void }) => {
  const nodes = useStore((s) => s.nodes);
  const edges = useStore((s) => s.edges);
  const sampleInput = useStore((s) => s.sampleInput);
  const groups = useStore((s) => s.groups);
  const openInNewTab = useStore((s) => s.openInNewTab);

  // Scenarios belong to the project they were saved in (E16-T5) — the same rule as the canvas.
  const { projectId } = useParams<{ projectId: string }>();

  const [templates, setTemplates] = useState<Template[]>([]);
  const [error, setError] = useState('');

  const refresh = () => listTemplates(projectId).then(setTemplates).catch((e) => setError(String(e)));
  useEffect(() => {
    refresh();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [projectId]);

  const save = async () => {
    const name = window.prompt('Name this scenario:', `Scenario ${new Date().toLocaleDateString()}`);
    if (!name) return;
    try {
      await saveTemplate(name, nodes, edges, sampleInput, projectId, groups);
      refresh();
    } catch (e) {
      setError(String(e));
    }
  };

  const load = (t: Template) => {
    openInNewTab(t.name, t.nodes as AppNode[], t.edges, t.sampleInput, t.groups);
    onClose();
  };

  const remove = async (id: string) => {
    await deleteTemplate(id);
    refresh();
  };

  return (
    <div className="fixed inset-0 z-50 bg-black/40 flex items-center justify-center p-4" onClick={onClose}>
      <div
        className={`bg-white dark:bg-slate-900 rounded-xl shadow-2xl w-[min(32rem,92vw)] h-[70vh] min-w-[320px] min-h-[220px] flex flex-col border border-slate-200 dark:border-slate-700 ${RESIZABLE_MODAL}`}
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between px-4 py-3 border-b border-slate-200 dark:border-slate-800 shrink-0">
          <h2 className="font-bold text-slate-800 dark:text-white flex items-center gap-2">
            <Clock size={16} /> Saved scenarios
          </h2>
          <div className="flex items-center gap-2">
            <button
              onClick={save}
              className="text-xs font-semibold text-emerald-600 dark:text-emerald-400 px-2.5 py-1.5 rounded-full border border-emerald-200 dark:border-emerald-500/30 hover:bg-emerald-50 dark:hover:bg-emerald-500/10 flex items-center gap-1"
              title="Save the current canvas + sample input"
            >
              <Save size={13} /> Save current
            </button>
            <button onClick={onClose} className="p-1.5 rounded-full hover:bg-slate-100 dark:hover:bg-slate-800 text-slate-500">
              <X size={18} />
            </button>
          </div>
        </div>

        <div className="flex-1 min-h-0 overflow-y-auto p-3">
          {error && <p className="text-xs text-red-500 mb-2">{error}</p>}
          {templates.length === 0 ? (
            <p className="text-sm text-slate-400 text-center py-8">
              No saved scenarios yet. Build a mapping and click <span className="font-semibold">Save current</span>.
            </p>
          ) : (
            <ul className="flex flex-col gap-1">
              {templates.map((t) => (
                <li
                  key={t.id}
                  className="group flex items-center justify-between gap-2 px-2.5 py-2 rounded-lg hover:bg-slate-100 dark:hover:bg-slate-800"
                >
                  <div className="min-w-0">
                    <div className="text-sm font-medium text-slate-700 dark:text-slate-200 truncate">{t.name}</div>
                    <div className="text-[10px] text-slate-400">
                      {when(t.createdAt)} · {t.nodes.length} nodes
                    </div>
                  </div>
                  <div className="flex items-center gap-1 shrink-0">
                    <button
                      onClick={() => load(t)}
                      className="text-xs font-semibold text-indigo-600 dark:text-indigo-400 px-2 py-1 rounded-md hover:bg-indigo-50 dark:hover:bg-indigo-500/10 flex items-center gap-1"
                    >
                      <Download size={12} /> Load
                    </button>
                    <button
                      onClick={() => remove(t.id)}
                      className="p-1 rounded-md text-slate-300 hover:text-red-500"
                      title="Delete"
                    >
                      <Trash2 size={14} />
                    </button>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>
    </div>
  );
};
