import { Handle, Position } from '@xyflow/react';
import { Plus, X } from 'lucide-react';
import { useStore } from '../../../store/useStore';

type SortKey = { by: string; order: string };

export const SortNode = ({ data, id }: any) => {
  const updateNodeData = useStore((s) => s.updateNodeData);
  const keys: SortKey[] = data.keys ?? [{ by: '', order: 'asc' }];
  const field =
    'nodrag min-w-0 bg-slate-50 dark:bg-slate-900 text-slate-800 dark:text-slate-100 border border-slate-200 dark:border-slate-700 rounded px-1 py-0.5 text-[10px] outline-none focus:ring-1 focus:ring-teal-500';

  const setKey = (index: number, patch: Partial<SortKey>) =>
    updateNodeData(id, { keys: keys.map((k, i) => (i === index ? { ...k, ...patch } : k)) });
  const addKey = () => updateNodeData(id, { keys: [...keys, { by: '', order: 'asc' }] });
  const removeKey = (index: number) => updateNodeData(id, { keys: keys.filter((_, i) => i !== index) });

  return (
    <div className="bg-white dark:bg-slate-800 border border-teal-500 rounded-lg shadow-md w-52 transition-colors relative">
      <Handle type="target" position={Position.Left} id="in" className="w-3 h-3 bg-teal-500! border-2 border-white dark:border-slate-800!" />
      <div className="bg-teal-500 text-white px-2 py-1 font-semibold text-[11px] rounded-t-lg">Sort</div>
      <div className="p-1.5 flex flex-col gap-1">
        {keys.map((key, index) => (
          <div key={index} className="flex items-center gap-1">
            <input
              className={field + ' flex-1'}
              placeholder="field (blank = item)"
              value={key.by}
              onChange={(e) => setKey(index, { by: e.target.value })}
            />
            <select className={field} value={key.order} onChange={(e) => setKey(index, { order: e.target.value })}>
              <option value="asc">▲ asc</option>
              <option value="desc">▼ desc</option>
            </select>
            {keys.length > 1 && (
              <button className="nodrag text-slate-300 hover:text-red-500" title="Remove key" onClick={() => removeKey(index)}>
                <X size={11} />
              </button>
            )}
          </div>
        ))}
        <button className="nodrag flex items-center justify-center gap-1 text-[10px] text-teal-600 hover:text-teal-700" onClick={addKey}>
          <Plus size={11} /> Add key
        </button>
      </div>
      <Handle type="source" position={Position.Right} id="out" className="w-3 h-3 bg-teal-500! border-2 border-white dark:border-slate-800!" />
    </div>
  );
};
