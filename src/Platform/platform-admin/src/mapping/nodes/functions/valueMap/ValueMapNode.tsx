import { Handle, Position } from '@xyflow/react';
import { Plus, X } from 'lucide-react';
import { useStore } from '../../../store/useStore';

type Pair = { key: string; value: string };

export const ValueMapNode = ({ data, id }: any) => {
  const updateNodeData = useStore((s) => s.updateNodeData);
  const pairs: Pair[] = data.pairs ?? [{ key: '1', value: 'Jan' }];

  const setPair = (index: number, patch: Partial<Pair>) => {
    const next = pairs.map((p, i) => (i === index ? { ...p, ...patch } : p));
    updateNodeData(id, { pairs: next });
  };
  const addPair = () => updateNodeData(id, { pairs: [...pairs, { key: '', value: '' }] });
  const removePair = (index: number) =>
    updateNodeData(id, { pairs: pairs.filter((_, i) => i !== index) });

  return (
    <div className="bg-white dark:bg-slate-800 border border-amber-500 rounded-lg shadow-md w-52 transition-colors relative">
      <Handle type="target" position={Position.Left} id="in" className="w-3 h-3 bg-amber-500! border-2 border-white dark:border-slate-800!" />
      <div className="bg-amber-500 text-white px-2 py-1 font-semibold text-[11px] rounded-t-lg">
        Value-Map (lookup)
      </div>
      <div className="p-1.5 flex flex-col gap-1">
        <div className="flex items-center gap-1 text-[9px] uppercase tracking-wide text-slate-400 px-0.5">
          <span className="flex-1">input</span>
          <span className="flex-1">output</span>
          <span className="w-3" />
        </div>
        {pairs.map((pair, index) => (
          <div key={index} className="flex items-center gap-1">
            <input
              className="nodrag flex-1 min-w-0 bg-slate-50 dark:bg-slate-900 text-slate-800 dark:text-slate-100 border border-slate-200 dark:border-slate-700 rounded px-1 py-0.5 text-[10px] outline-none focus:ring-1 focus:ring-amber-500"
              value={pair.key}
              onChange={(e) => setPair(index, { key: e.target.value })}
            />
            <span className="text-slate-400 text-[10px]">→</span>
            <input
              className="nodrag flex-1 min-w-0 bg-slate-50 dark:bg-slate-900 text-slate-800 dark:text-slate-100 border border-slate-200 dark:border-slate-700 rounded px-1 py-0.5 text-[10px] outline-none focus:ring-1 focus:ring-amber-500"
              value={pair.value}
              onChange={(e) => setPair(index, { value: e.target.value })}
            />
            <button
              className="nodrag text-slate-300 hover:text-red-500 transition-colors"
              title="Remove row"
              onClick={() => removePair(index)}
            >
              <X size={11} />
            </button>
          </div>
        ))}
        <button
          className="nodrag flex items-center justify-center gap-1 text-[10px] text-amber-600 hover:text-amber-700 mt-0.5"
          onClick={addPair}
        >
          <Plus size={11} /> Add row
        </button>
        <label className="text-[9px] uppercase tracking-wide text-slate-400 mt-1 px-0.5">default (no match)</label>
        <input
          className="nodrag bg-slate-50 dark:bg-slate-900 text-slate-800 dark:text-slate-100 border border-slate-200 dark:border-slate-700 rounded px-1 py-0.5 text-[10px] outline-none focus:ring-1 focus:ring-amber-500"
          placeholder="(optional)"
          value={data.default ?? ''}
          onChange={(e) => updateNodeData(id, { default: e.target.value })}
        />
      </div>
      <Handle type="source" position={Position.Right} id="out" className="w-3 h-3 bg-amber-500! border-2 border-white dark:border-slate-800!" />
    </div>
  );
};
