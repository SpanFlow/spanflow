import { X } from 'lucide-react';
import { useStore } from '../store/useStore';
import { DetailBlock, RESIZABLE_MODAL } from './DetailBlock';

const pretty = (v: unknown) => (v === undefined ? '-' : JSON.stringify(v, null, 2));

/** Resolve a dotted source path against the sample input (arrays fan out). */
function lookup(obj: any, path: string): unknown {
  return path.split('.').filter(Boolean).reduce((acc: any, key) => {
    if (acc == null) return undefined;
    if (Array.isArray(acc)) return acc.map((x) => x?.[key]);
    return acc[key];
  }, obj);
}

export const DebugModal = () => {
  const nodeId = useStore((s) => s.debug.inspectNodeId);
  const result = useStore((s) => (nodeId ? s.debug.results[nodeId] : undefined));
  const nodes = useStore((s) => s.nodes);
  const edges = useStore((s) => s.edges);
  const results = useStore((s) => s.debug.results);
  const sampleInput = useStore((s) => s.sampleInput);
  const close = useStore((s) => s.inspectNode);

  if (!nodeId || !result) return null;

  const node = nodes.find((n) => n.id === nodeId);
  const label = (node?.data?.label as string) || node?.type || nodeId;

  let sample: any = {};
  try {
    sample = JSON.parse(sampleInput);
  } catch {
    /* ignore */
  }

  const request = edges
    .filter((e) => e.target === nodeId)
    .map((e) => {
      const up = nodes.find((n) => n.id === e.source);
      const value =
        results[e.source]?.value ??
        (up?.type === 'source' && e.sourceHandle ? lookup(sample, e.sourceHandle) : undefined);
      return { handle: e.targetHandle || 'in', from: up?.type === 'source' ? e.sourceHandle : up?.data?.label || up?.type, value };
    });

  const isArray = Array.isArray(result.value);

  return (
    <div className="fixed inset-0 z-50 bg-black/40 flex items-center justify-center p-4" onClick={() => close(null)}>
      <div
        className={`bg-white dark:bg-slate-900 rounded-xl shadow-2xl w-[min(32rem,92vw)] h-[70vh] min-w-[320px] min-h-[220px] flex flex-col border border-slate-200 dark:border-slate-700 ${RESIZABLE_MODAL}`}
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between px-4 py-3 border-b border-slate-200 dark:border-slate-800 shrink-0">
          <div className="flex items-center gap-2 min-w-0">
            <h2 className="font-bold text-slate-800 dark:text-white truncate">{label}</h2>
            <span className="text-[10px] text-slate-400 tabular-nums shrink-0">⏱ {result.elapsedMs.toFixed(2)}ms</span>
            {isArray && (
              <span className="text-[10px] text-slate-400 shrink-0">{(result.value as unknown[]).length} results</span>
            )}
          </div>
          <button onClick={() => close(null)} className="p-1.5 rounded-full hover:bg-slate-100 dark:hover:bg-slate-800 text-slate-500">
            <X size={18} />
          </button>
        </div>

        <div className="flex-1 min-h-0 p-4 space-y-3 overflow-y-auto">
          {result.error ? (
            <DetailBlock title="Error" tone="error" format="text">
              {result.error}
            </DetailBlock>
          ) : (
            <>
              {request.length > 0 && (
                <DetailBlock title="Request (inputs)" format="text">
                  {request.map((r) => `${r.handle}${r.from ? ` ← ${r.from}` : ''}: ${pretty(r.value)}`).join('\n')}
                </DetailBlock>
              )}
              <DetailBlock title={isArray ? 'Response (one per item)' : 'Response'}>{pretty(result.value)}</DetailBlock>
            </>
          )}
        </div>
      </div>
    </div>
  );
};
