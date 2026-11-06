import React, { useState } from 'react';
import { PanelLeftClose, PanelLeftOpen, Plus, FunctionSquare, CircleDot, CornerDownLeft, ChevronDown, ChevronRight } from 'lucide-react';
import { sidebarGroups } from '../nodes/registry';
import { useStore } from '../store/useStore';

// Static class strings so Tailwind keeps them at build time.
const COLOR: Record<string, { icon: string; hover: string }> = {
  indigo: { icon: 'bg-indigo-100 dark:bg-indigo-900/30 text-indigo-600 dark:text-indigo-400', hover: 'hover:border-indigo-500 dark:hover:border-indigo-400' },
  purple: { icon: 'bg-purple-100 dark:bg-purple-900/30 text-purple-600 dark:text-purple-400', hover: 'hover:border-purple-500 dark:hover:border-purple-400' },
  emerald: { icon: 'bg-emerald-100 dark:bg-emerald-900/30 text-emerald-600 dark:text-emerald-400', hover: 'hover:border-emerald-500 dark:hover:border-emerald-400' },
  amber: { icon: 'bg-amber-100 dark:bg-amber-900/30 text-amber-600 dark:text-amber-400', hover: 'hover:border-amber-500 dark:hover:border-amber-400' },
  teal: { icon: 'bg-teal-100 dark:bg-teal-900/30 text-teal-600 dark:text-teal-400', hover: 'hover:border-teal-500 dark:hover:border-teal-400' },
  cyan: { icon: 'bg-cyan-100 dark:bg-cyan-900/30 text-cyan-600 dark:text-cyan-400', hover: 'hover:border-cyan-500 dark:hover:border-cyan-400' },
  orange: { icon: 'bg-orange-100 dark:bg-orange-900/30 text-orange-600 dark:text-orange-400', hover: 'hover:border-orange-500 dark:hover:border-orange-400' },
  violet: { icon: 'bg-violet-100 dark:bg-violet-900/30 text-violet-600 dark:text-violet-400', hover: 'hover:border-violet-500 dark:hover:border-violet-400' },
  lime: { icon: 'bg-lime-100 dark:bg-lime-900/30 text-lime-700 dark:text-lime-400', hover: 'hover:border-lime-600 dark:hover:border-lime-500' },
  pink: { icon: 'bg-pink-100 dark:bg-pink-900/30 text-pink-600 dark:text-pink-400', hover: 'hover:border-pink-500 dark:hover:border-pink-400' },
  red: { icon: 'bg-red-100 dark:bg-red-900/30 text-red-600 dark:text-red-400', hover: 'hover:border-red-500 dark:hover:border-red-400' },
  rose: { icon: 'bg-rose-100 dark:bg-rose-900/30 text-rose-600 dark:text-rose-400', hover: 'hover:border-rose-500 dark:hover:border-rose-400' },
  sky: { icon: 'bg-sky-100 dark:bg-sky-900/30 text-sky-600 dark:text-sky-400', hover: 'hover:border-sky-500 dark:hover:border-sky-400' },
};

export const Sidebar = () => {
  const [collapsed, setCollapsed] = useState(false);
  const [collapsedGroups, setCollapsedGroups] = useState<Set<string>>(new Set());
  const toggleGroup = (title: string) =>
    setCollapsedGroups((prev) => {
      const next = new Set(prev);
      if (!next.delete(title)) next.add(title);
      return next;
    });
  const docs = useStore((s) => s.docs);
  const activeDocId = useStore((s) => s.activeDocId);
  const newFunction = useStore((s) => s.newFunction);
  const switchTab = useStore((s) => s.switchTab);

  const activeKind = docs.find((d) => d.id === activeDocId)?.kind ?? 'mapping';
  const functionDocs = docs.filter((d) => d.kind === 'function');

  const onDragStart = (event: React.DragEvent, nodeType: string) => {
    event.dataTransfer.setData('application/reactflow', nodeType);
    event.dataTransfer.effectAllowed = 'move';
  };

  if (collapsed) {
    return (
      <div className="w-10 bg-white dark:bg-slate-900 border-r border-slate-200 dark:border-slate-800 flex flex-col items-center py-3 shrink-0 z-10 transition-colors">
        <button
          onClick={() => setCollapsed(false)}
          className="p-2 rounded-md text-slate-500 hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors"
          title="Show node palette"
        >
          <PanelLeftOpen size={18} />
        </button>
      </div>
    );
  }

  return (
    <div className="w-52 bg-white dark:bg-slate-900 border-r border-slate-200 dark:border-slate-800 flex flex-col p-3 shrink-0 shadow-xl z-10 transition-colors overflow-y-auto">
      <div className="flex items-center justify-between mb-4">
        <h2 className="text-base font-bold text-slate-800 dark:text-slate-100 flex items-center gap-2">
          <svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg" className="w-5 h-5 stroke-indigo-500 stroke-2">
            <path d="M14 2L8 10H14L10 22L16 14H10L14 2Z" fill="currentColor" fillOpacity="0.2" />
          </svg>
          MapLite
        </h2>
        <button
          onClick={() => setCollapsed(true)}
          className="p-1.5 rounded-md text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors"
          title="Hide panel"
        >
          <PanelLeftClose size={16} />
        </button>
      </div>

      <div className="flex flex-col gap-3">
        {sidebarGroups.map((group) => {
          const open = !collapsedGroups.has(group.title);
          return (
            <div key={group.title}>
              <button
                onClick={() => toggleGroup(group.title)}
                className="w-full flex items-center gap-1 mb-1.5 px-0.5 text-[10px] uppercase font-semibold text-slate-400 tracking-wider hover:text-slate-600 dark:hover:text-slate-200"
              >
                {open ? <ChevronDown size={11} /> : <ChevronRight size={11} />}
                {group.title}
              </button>
              {open && (
                <div className="flex flex-col gap-1.5">
                  {group.items.map(({ type, label, icon: Icon, color }) => (
                    <div
                      key={type}
                      className={`flex items-center gap-1.5 px-2 py-1.5 bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-md cursor-grab transition-all hover:shadow-sm ${COLOR[color].hover}`}
                      onDragStart={(event) => onDragStart(event, type)}
                      draggable
                      title={`Drag ${label} onto the canvas`}
                    >
                      <div className={`p-1 rounded ${COLOR[color].icon}`}>
                        <Icon size={13} />
                      </div>
                      <span className="text-[11px] font-medium text-slate-700 dark:text-slate-300 truncate">{label}</span>
                    </div>
                  ))}
                </div>
              )}
            </div>
          );
        })}
        {/* Custom functions */}
        <div>
          <div className="flex items-center justify-between mb-1.5 px-0.5">
            <button
              onClick={() => toggleGroup('Custom functions')}
              className="flex items-center gap-1 text-[10px] uppercase font-semibold text-slate-400 tracking-wider hover:text-slate-600 dark:hover:text-slate-200"
            >
              {collapsedGroups.has('Custom functions') ? <ChevronRight size={11} /> : <ChevronDown size={11} />}
              User defined functions
            </button>
            <button
              onClick={() => switchTab(newFunction('New function'))}
              className="text-slate-400 hover:text-fuchsia-500"
              title="Create a new custom function"
            >
              <Plus size={14} />
            </button>
          </div>

          {!collapsedGroups.has('Custom functions') && (
            <>
              {/* Inside a function document: Parameter + Return building blocks. */}
              {activeKind === 'function' && (
                <div className="flex flex-col gap-1.5 mb-1.5">
                  {[
                    { type: 'param', label: 'Parameter', Icon: CircleDot, color: 'amber' as const },
                    { type: 'return', label: 'Return', Icon: CornerDownLeft, color: 'emerald' as const },
                  ].map(({ type, label, Icon, color }) => (
                    <div
                      key={type}
                      draggable
                      onDragStart={(e) => onDragStart(e, type)}
                      className={`flex items-center gap-1.5 px-2 py-1.5 bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-md cursor-grab transition-all hover:shadow-sm ${COLOR[color].hover}`}
                    >
                      <div className={`p-1 rounded ${COLOR[color].icon}`}>
                        <Icon size={13} />
                      </div>
                      <span className="text-[11px] font-medium text-slate-700 dark:text-slate-300 truncate">{label}</span>
                    </div>
                  ))}
                </div>
              )}

              {functionDocs.length === 0 ? (
                <p className="text-[10px] text-slate-400 px-0.5 leading-snug">
                  None yet: open Examples for ready-made functions, or ＋ to build one.
                </p>
              ) : (
                <div className="flex flex-col gap-1.5">
                  {functionDocs.map((f) => (
                    <div
                      key={f.id}
                      draggable
                      onDragStart={(e) => onDragStart(e, `functionCall:${f.id}`)}
                      className={`flex items-center gap-1.5 px-2 py-1.5 bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-md cursor-grab transition-all hover:shadow-sm ${COLOR.pink.hover}`}
                      title={`Drag ${f.name}() onto the canvas`}
                    >
                      <div className={`p-1 rounded ${COLOR.pink.icon}`}>
                        <FunctionSquare size={13} />
                      </div>
                      <span className="text-[11px] font-medium text-slate-700 dark:text-slate-300 truncate">{f.name}</span>
                    </div>
                  ))}
                </div>
              )}
            </>
          )}
        </div>
      </div>

      <div className="mt-auto pt-4">
        <div className="p-2.5 bg-indigo-50 dark:bg-indigo-900/20 rounded-lg">
          <p className="text-[10px] text-indigo-600 dark:text-indigo-300/80 leading-snug">
            Drag nodes onto the canvas and connect their dots left-to-right to build a pipeline.
          </p>
        </div>
      </div>
    </div>
  );
};
