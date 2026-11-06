import { HelpCircle, X } from 'lucide-react';
import type { NodeHelp } from '../nodes/base';
import { specByType } from '../nodes/registry';
import { useStore } from '../store/useStore';

/** The help for a node, preferring settings-aware help (e.g. the chosen function). */
const helpForNode = (type: string, data: Record<string, any>): NodeHelp | undefined => {
  const spec = specByType[type];
  return spec?.helpFor?.(data ?? {}) ?? spec?.help;
};

/**
 * The ⓘ button in a node's header. Node docs used to sit on the node itself,
 * which made every node tall and left no room for its ports; they open here now.
 */
export const NodeHelpButton = ({ id, type }: { id: string; type: string }) => {
  const showHelp = useStore((s) => s.showHelp);
  if (!specByType[type]?.help && !specByType[type]?.helpFor) return null;

  return (
    <button
      className="nodrag ml-auto opacity-70 hover:opacity-100"
      title="What does this node do?"
      onClick={(e) => {
        e.stopPropagation();
        showHelp(id);
      }}
    >
      <HelpCircle size={12} />
    </button>
  );
};

/** The help modal itself. Rendered once, over the canvas. */
export const NodeHelpModal = () => {
  const helpNodeId = useStore((s) => s.helpNodeId);
  const showHelp = useStore((s) => s.showHelp);
  const node = useStore((s) => s.nodes.find((n) => n.id === s.helpNodeId));

  if (!helpNodeId || !node) return null;

  const spec = specByType[node.type as string];
  const help = helpForNode(node.type as string, node.data as Record<string, any>);
  if (!spec || !help) return null;

  const Icon = spec.icon;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/40 p-4" onClick={() => showHelp(null)}>
      <div
        className="w-full max-w-lg max-h-[80vh] overflow-y-auto rounded-lg bg-white dark:bg-slate-800 shadow-xl border border-slate-200 dark:border-slate-700"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center gap-2 border-b border-slate-100 dark:border-slate-700 px-4 py-3">
          <Icon size={16} className="text-slate-500 dark:text-slate-400" />
          <h2 className="font-semibold text-sm text-slate-800 dark:text-slate-100">{spec.label}</h2>
          <button
            className="ml-auto text-slate-400 hover:text-slate-600 dark:hover:text-slate-200"
            title="Close"
            onClick={() => showHelp(null)}
          >
            <X size={16} />
          </button>
        </div>

        <div className="px-4 py-3 flex flex-col gap-3">
          <p className="text-xs leading-relaxed text-slate-700 dark:text-slate-200">{help.summary}</p>

          {help.details && help.details.length > 0 && (
            <ul className="flex flex-col gap-1.5 list-disc pl-4">
              {help.details.map((line, i) => (
                <li key={i} className="text-xs leading-relaxed text-slate-600 dark:text-slate-300">
                  {line}
                </li>
              ))}
            </ul>
          )}

          {help.example && (
            <div className="flex flex-col gap-1">
              <span className="text-[10px] uppercase tracking-wide text-slate-400">{help.example.caption}</span>
              <pre className="overflow-x-auto rounded bg-slate-50 dark:bg-slate-900 border border-slate-200 dark:border-slate-700 p-2 text-[10px] leading-relaxed text-slate-700 dark:text-slate-200">
                {help.example.code}
              </pre>
            </div>
          )}
        </div>
      </div>
    </div>
  );
};
