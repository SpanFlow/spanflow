import { Handle, Position } from '@xyflow/react';
import { useStore } from '../../../store/useStore';
import { AGGREGATES } from './aggregates';
import { NodeHelpButton } from '../../../components/NodeHelp';

export const AggregateNode = ({ data, id }: any) => {
  const updateNodeData = useStore((s) => s.updateNodeData);
  const fn = data.function || 'sum';

  return (
    <div className="bg-white dark:bg-slate-800 border border-emerald-500 rounded-lg shadow-md w-48 transition-colors relative">
      <Handle type="target" position={Position.Left} id="in" className="w-3 h-3 bg-emerald-500! border-2 border-white dark:border-slate-800!" />
      <div className="bg-emerald-500 text-white px-2 py-1 font-semibold text-[11px] flex justify-between items-center rounded-t-lg">
        <span>Aggregate</span>
        <NodeHelpButton id={id} type="aggregate" />
      </div>
      <div className="p-1.5 flex flex-col gap-1.5">
        <select
          className="nodrag bg-slate-50 dark:bg-slate-900 text-slate-800 dark:text-slate-100 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-0.5 text-[10px] outline-none focus:ring-1 focus:ring-emerald-500"
          value={fn}
          onChange={(e) => updateNodeData(id, { function: e.target.value })}
        >
          {AGGREGATES.map((a) => (
            <option key={a.value} value={a.value}>{a.label}</option>
          ))}
        </select>

        {fn === 'joinString' && (
          <input
            className="nodrag bg-slate-50 dark:bg-slate-900 text-slate-800 dark:text-slate-100 border border-slate-200 dark:border-slate-700 rounded px-1.5 py-0.5 text-[10px] outline-none focus:ring-1 focus:ring-emerald-500"
            placeholder="separator (, )"
            value={data.separator ?? ''}
            onChange={(e) => updateNodeData(id, { separator: e.target.value })}
          />
        )}

      </div>
      <Handle type="source" position={Position.Right} id="out" className="w-3 h-3 bg-emerald-500! border-2 border-white dark:border-slate-800!" />
    </div>
  );
};
