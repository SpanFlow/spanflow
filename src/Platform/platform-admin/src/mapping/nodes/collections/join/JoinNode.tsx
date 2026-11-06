import { Handle, Position } from '@xyflow/react';
import { useStore } from '../../../store/useStore';
import { NodeHelpButton } from '../../../components/NodeHelp';

export const JoinNode = ({ data, id }: any) => {
  const updateNodeData = useStore((s) => s.updateNodeData);
  const field =
    'nodrag min-w-0 flex-1 bg-slate-50 dark:bg-slate-900 text-slate-800 dark:text-slate-100 border border-slate-200 dark:border-slate-700 rounded px-1 py-0.5 text-[10px] outline-none focus:ring-1 focus:ring-pink-500';
  const dot = 'w-3 h-3 bg-pink-500! border-2 border-white dark:border-slate-800!';

  return (
    <div className="bg-white dark:bg-slate-800 border border-pink-500 rounded-lg shadow-md w-56 transition-colors">
      <div className="bg-pink-500 text-white px-2 py-1 font-semibold text-[11px] rounded-t-lg flex items-center gap-1">
        Join
        <NodeHelpButton id={id} type="join" />
      </div>
      <div className="p-1.5 flex flex-col gap-1">
        <div className="relative flex items-center gap-1">
          <Handle type="target" position={Position.Left} id="left" className={dot} />
          <span className="text-[9px] uppercase tracking-wide text-slate-400 w-8">left</span>
          <input className={field} placeholder="left key" value={data.leftKey ?? ''} onChange={(e) => updateNodeData(id, { leftKey: e.target.value })} />
        </div>
        <div className="relative flex items-center gap-1">
          <Handle type="target" position={Position.Left} id="right" className={dot} />
          <span className="text-[9px] uppercase tracking-wide text-slate-400 w-8">right</span>
          <input className={field} placeholder="right key" value={data.rightKey ?? ''} onChange={(e) => updateNodeData(id, { rightKey: e.target.value })} />
        </div>
      </div>
      <Handle type="source" position={Position.Right} id="out" className={dot} />
    </div>
  );
};
