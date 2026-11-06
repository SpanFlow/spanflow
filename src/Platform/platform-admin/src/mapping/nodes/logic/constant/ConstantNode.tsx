import { Handle, Position } from '@xyflow/react';
import { useStore } from '../../../store/useStore';

export const ConstantNode = ({ data, id }: any) => {
  const updateNodeData = useStore((s) => s.updateNodeData);

  return (
    <div className="bg-white dark:bg-slate-800 border border-sky-500 rounded-lg shadow-md w-36 transition-colors relative">
      <div className="bg-sky-500 text-white px-2 py-1 font-semibold text-[11px] rounded-t-lg">Constant</div>
      <div className="p-1.5">
        <input
          className="nodrag w-full bg-slate-50 dark:bg-slate-900 text-slate-800 dark:text-slate-100 border border-slate-200 dark:border-slate-700 rounded px-1 py-0.5 text-[10px] outline-none focus:ring-1 focus:ring-sky-500"
          placeholder="value"
          value={data.value ?? ''}
          onChange={(e) => updateNodeData(id, { value: e.target.value })}
        />
      </div>
      <Handle type="source" position={Position.Right} id="out" className="w-3 h-3 bg-sky-500! border-2 border-white dark:border-slate-800!" />
    </div>
  );
};
