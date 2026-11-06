import { Handle, Position } from '@xyflow/react';
import { Plus, X } from 'lucide-react';
import { useStore } from '../../../store/useStore';
import { NodeHelpButton } from '../../../components/NodeHelp';

type Row = { name: string; agg: string; field: string };

const AGGS = ['key', 'sum', 'count', 'avg', 'min', 'max', 'first', 'last', 'items'];
const needsField = (agg: string) => agg !== 'key' && agg !== 'count';

export const GroupByNode = ({ data, id }: any) => {
  const updateNodeData = useStore((s) => s.updateNodeData);
  const edges = useStore((s) => s.edges);
  const isWired = (port: string) => edges.some((e) => e.target === id && e.targetHandle === port);
  const rows: Row[] = data.rows ?? [{ name: 'key', agg: 'key', field: '' }];
  const field =
    'nodrag min-w-0 bg-slate-50 dark:bg-slate-900 text-slate-800 dark:text-slate-100 border border-slate-200 dark:border-slate-700 rounded px-1 py-0.5 text-[10px] outline-none focus:ring-1 focus:ring-lime-600';

  const setRow = (index: number, patch: Partial<Row>) =>
    updateNodeData(id, { rows: rows.map((r, i) => (i === index ? { ...r, ...patch } : r)) });
  const addRow = () => updateNodeData(id, { rows: [...rows, { name: '', agg: 'sum', field: '' }] });
  const removeRow = (index: number) => updateNodeData(id, { rows: rows.filter((_, i) => i !== index) });

  return (
    <div className="bg-white dark:bg-slate-800 border border-lime-600 rounded-lg shadow-md w-60 transition-colors relative">
      <Handle type="target" position={Position.Left} id="in" style={{ top: 14 }} className="w-3 h-3 bg-lime-600! border-2 border-white dark:border-slate-800!" />

      <div className="bg-lime-600 text-white px-2 py-1 font-semibold text-[11px] rounded-t-lg flex items-center gap-1">
        Group By
        <NodeHelpButton id={id} type="groupBy" />
      </div>
      <div className="p-1.5 flex flex-col gap-1">
        <div className="flex items-center gap-1">
          <span className="text-[9px] uppercase tracking-wide text-slate-400 w-8">key</span>
          <input className={field + ' flex-1'} placeholder="group key field" value={data.key ?? ''} onChange={(e) => updateNodeData(id, { key: e.target.value })} />
        </div>
        {rows.map((row, index) => (
          <div key={index} className="flex items-center gap-1">
            <input className={field + ' flex-1'} placeholder="out field" value={row.name} onChange={(e) => setRow(index, { name: e.target.value })} />
            <select className={field} value={row.agg} onChange={(e) => setRow(index, { agg: e.target.value })}>
              {AGGS.map((a) => (<option key={a} value={a}>{a}</option>))}
            </select>
            {needsField(row.agg) && (
              <input className={field + ' w-12'} placeholder="of" value={row.field} onChange={(e) => setRow(index, { field: e.target.value })} />
            )}
            {rows.length > 1 && (
              <button className="nodrag text-slate-300 hover:text-red-500" title="Remove field" onClick={() => removeRow(index)}>
                <X size={11} />
              </button>
            )}
          </div>
        ))}
        <button className="nodrag flex items-center justify-center gap-1 text-[10px] text-lime-700 hover:text-lime-800" onClick={addRow}>
          <Plus size={11} /> Add field
        </button>

        {/* Optional predicate ports, each on its own row so the handle has a
            place to sit. Wire a Where node in to use one; leave both empty and
            the node groups everything, exactly as it did before. */}
        <div className="border-t border-slate-100 dark:border-slate-700/50 pt-1 flex flex-col gap-1">
          {(
            [
              ['where', 'bg-amber-500!', 'text-amber-600', 'filter rows before grouping'],
              ['having', 'bg-amber-600!', 'text-amber-700', 'drop whole groups after'],
            ] as const
          ).map(([port, dot, text, caption]) => (
            <div key={port} className="relative flex items-center gap-1 h-4">
              <Handle
                type="target"
                position={Position.Left}
                id={port}
                className={`w-3 h-3 ${dot} border-2 border-white dark:border-slate-800!`}
                style={{ left: -12, top: '50%' }}
              />
              <span className={`text-[9px] uppercase tracking-wide font-mono ${text} ${isWired(port) ? '' : 'opacity-50'}`}>
                {port}
              </span>
              <span className="text-[9px] text-slate-400 truncate">{isWired(port) ? '' : caption}</span>
            </div>
          ))}
        </div>
      </div>
      <Handle type="source" position={Position.Right} id="out" className="w-3 h-3 bg-lime-600! border-2 border-white dark:border-slate-800!" />
    </div>
  );
};
