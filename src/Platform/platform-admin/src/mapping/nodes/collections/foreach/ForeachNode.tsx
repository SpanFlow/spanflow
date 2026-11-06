import { Handle, Position } from '@xyflow/react';
import { Plus, X } from 'lucide-react';
import { useStore } from '../../../store/useStore';
import { NodeHelpButton } from '../../../components/NodeHelp';

type Row = { name: string; from: string };

export const ForeachNode = ({ data, id }: any) => {
  const updateNodeData = useStore((s) => s.updateNodeData);
  const rows: Row[] = data.rows ?? [{ name: '', from: '' }];
  const field =
    'nodrag min-w-0 bg-slate-50 dark:bg-slate-900 text-slate-800 dark:text-slate-100 border border-slate-200 dark:border-slate-700 rounded px-1 py-0.5 text-[10px] outline-none focus:ring-1 focus:ring-violet-500';

  const setRow = (index: number, patch: Partial<Row>) =>
    updateNodeData(id, { rows: rows.map((r, i) => (i === index ? { ...r, ...patch } : r)) });
  const addRow = () => updateNodeData(id, { rows: [...rows, { name: '', from: '' }] });
  const removeRow = (index: number) => updateNodeData(id, { rows: rows.filter((_, i) => i !== index) });

  return (
    <div className="bg-white dark:bg-slate-800 border border-violet-500 rounded-lg shadow-md w-56 transition-colors relative">
      <Handle type="target" position={Position.Left} id="in" className="w-3 h-3 bg-violet-500! border-2 border-white dark:border-slate-800!" />
      <div className="bg-violet-500 text-white px-2 py-1 font-semibold text-[11px] rounded-t-lg flex items-center gap-1">
        For Each
        <NodeHelpButton id={id} type="foreach" />
      </div>
      <div className="p-1.5 flex flex-col gap-1">
        <div className="flex items-center gap-1 text-[9px] uppercase tracking-wide text-slate-400 px-0.5">
          <span className="flex-1">output field</span>
          <span className="flex-1">from item</span>
          <span className="w-3" />
        </div>
        {rows.map((row, index) => (
          <div key={index} className="flex items-center gap-1">
            <input className={field + ' flex-1'} placeholder="name" value={row.name} onChange={(e) => setRow(index, { name: e.target.value })} />
            <input className={field + ' flex-1'} placeholder="path ($=item)" value={row.from} onChange={(e) => setRow(index, { from: e.target.value })} />
            {rows.length > 1 && (
              <button className="nodrag text-slate-300 hover:text-red-500" title="Remove field" onClick={() => removeRow(index)}>
                <X size={11} />
              </button>
            )}
          </div>
        ))}
        <button className="nodrag flex items-center justify-center gap-1 text-[10px] text-violet-600 hover:text-violet-700" onClick={addRow}>
          <Plus size={11} /> Add field
        </button>
      </div>
      <Handle type="source" position={Position.Right} id="out" className="w-3 h-3 bg-violet-500! border-2 border-white dark:border-slate-800!" />
    </div>
  );
};
