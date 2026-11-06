import { Handle, Position } from '@xyflow/react';
import { useStore } from '../../../store/useStore';
import { NodeHelpButton } from '../../../components/NodeHelp';

export const FilterNode = ({ data, id }: any) => {
  const updateNodeData = useStore((s) => s.updateNodeData);
  const field =
    'nodrag min-w-0 bg-slate-50 dark:bg-slate-900 text-slate-800 dark:text-slate-100 border border-slate-200 dark:border-slate-700 rounded px-1 py-0.5 text-[10px] outline-none focus:ring-1 focus:ring-orange-500';

  return (
    <div className="bg-white dark:bg-slate-800 border border-orange-500 rounded-lg shadow-md w-52 transition-colors relative">
      <Handle type="target" position={Position.Left} id="in" className="w-3 h-3 bg-orange-500! border-2 border-white dark:border-slate-800!" />
      <div className="bg-orange-500 text-white px-2 py-1 font-semibold text-[11px] rounded-t-lg flex items-center gap-1">
        Filter
        <NodeHelpButton id={id} type="filter" />
      </div>
      <div className="p-1.5 flex flex-col gap-1">
        <div className="flex items-center gap-1">
          <input
            className={field + ' flex-1'}
            placeholder="field"
            value={data.field ?? ''}
            onChange={(e) => updateNodeData(id, { field: e.target.value })}
          />
          <select className={field} value={data.op || 'eq'} onChange={(e) => updateNodeData(id, { op: e.target.value })}>
            <option value="eq">=</option>
            <option value="neq">≠</option>
            <option value="gt">&gt;</option>
            <option value="gte">≥</option>
            <option value="lt">&lt;</option>
            <option value="lte">≤</option>
            <option value="contains">has</option>
          </select>
          <input
            className={field + ' flex-1'}
            placeholder="value"
            value={data.value ?? ''}
            onChange={(e) => updateNodeData(id, { value: e.target.value })}
          />
        </div>
      </div>
      <Handle type="source" position={Position.Right} id="out" className="w-3 h-3 bg-orange-500! border-2 border-white dark:border-slate-800!" />
    </div>
  );
};
