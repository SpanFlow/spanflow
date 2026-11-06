import { useState } from 'react';
import { X, Plus } from 'lucide-react';
import { useStore } from '../store/useStore';

export const DocumentTabs = () => {
  const docs = useStore((s) => s.docs);
  const activeDocId = useStore((s) => s.activeDocId);
  const switchTab = useStore((s) => s.switchTab);
  const closeTab = useStore((s) => s.closeTab);
  const renameTab = useStore((s) => s.renameTab);
  const newTab = useStore((s) => s.newTab);
  const [editing, setEditing] = useState<string | null>(null);

  const commit = (id: string, value: string, fallback: string) => {
    renameTab(id, value.trim() || fallback);
    setEditing(null);
  };

  return (
    <div className="flex items-center gap-1 px-2 h-9 bg-white dark:bg-slate-900 border-b border-slate-200 dark:border-slate-800 shrink-0 overflow-x-auto">
      {docs.map((d) => (
        <div
          key={d.id}
          onClick={() => switchTab(d.id)}
          onDoubleClick={() => setEditing(d.id)}
          className={`group flex items-center gap-1 px-2.5 h-7 rounded-md text-xs cursor-pointer shrink-0 transition-colors ${
            d.id === activeDocId
              ? 'bg-indigo-50 dark:bg-indigo-500/15 text-indigo-700 dark:text-indigo-300 font-semibold'
              : 'text-slate-500 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800'
          }`}
          title="Double-click to rename"
        >
          {d.kind === 'function' && <span className="text-[10px] italic opacity-70">ƒ</span>}
          {editing === d.id ? (
            <input
              autoFocus
              defaultValue={d.name}
              onClick={(e) => e.stopPropagation()}
              onBlur={(e) => commit(d.id, e.target.value, d.name)}
              onKeyDown={(e) => {
                if (e.key === 'Enter') commit(d.id, (e.target as HTMLInputElement).value, d.name);
                if (e.key === 'Escape') setEditing(null);
              }}
              className="bg-transparent outline-none w-24 border-b border-indigo-400"
            />
          ) : (
            <span className="truncate max-w-[10rem]">{d.name}</span>
          )}
          {docs.length > 1 && (
            <button
              className="opacity-0 group-hover:opacity-100 text-slate-400 hover:text-red-500 transition-opacity"
              onClick={(e) => {
                e.stopPropagation();
                closeTab(d.id);
              }}
              title="Close tab"
            >
              <X size={11} />
            </button>
          )}
        </div>
      ))}
      <button
        onClick={() => newTab('mapping', 'Untitled')}
        className="p-1 rounded-md text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800 shrink-0"
        title="New mapping tab"
      >
        <Plus size={14} />
      </button>
    </div>
  );
};
