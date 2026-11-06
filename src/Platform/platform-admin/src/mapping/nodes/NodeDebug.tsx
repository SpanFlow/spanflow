import { Eye, AlertTriangle } from 'lucide-react';
import { useStore } from '../store/useStore';

/**
 * A compact debug chip under a node when a debug run is active: it shows only
 * the timing (and an error flag), plus an eye button that opens the full
 * request/response in a modal — so long values never widen the node.
 */
export const NodeDebug = ({ nodeId }: { nodeId: string }) => {
  const active = useStore((s) => s.debug.active);
  const result = useStore((s) => s.debug.results[nodeId]);
  const inspect = useStore((s) => s.inspectNode);

  if (!active || !result) return null;
  const err = !!result.error;
  const count = Array.isArray(result.value) ? result.value.length : undefined;

  return (
    <button
      onClick={() => inspect(nodeId)}
      className={`nodrag mt-1 w-full flex items-center justify-between gap-2 px-1.5 py-0.5 rounded-md border text-[9px] transition-colors ${
        err
          ? 'border-red-300 dark:border-red-500/40 bg-red-50 dark:bg-red-500/10 text-red-600 dark:text-red-400'
          : 'border-slate-200 dark:border-slate-700 bg-slate-50 dark:bg-slate-900/70 text-slate-500 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800'
      }`}
      title="Inspect this node's request & response"
    >
      <span className="flex items-center gap-1 truncate">
        {err ? <AlertTriangle size={10} /> : <Eye size={10} />}
        {err ? 'error' : count !== undefined ? `${count} result${count === 1 ? '' : 's'}` : 'inspect'}
      </span>
      <span className="tabular-nums shrink-0">⏱ {result.elapsedMs.toFixed(1)}ms</span>
    </button>
  );
};
