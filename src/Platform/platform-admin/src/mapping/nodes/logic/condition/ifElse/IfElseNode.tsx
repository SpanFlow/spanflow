import { useEffect } from 'react';
import { Handle, Position, useUpdateNodeInternals } from '@xyflow/react';
import { Plus, X } from 'lucide-react';
import { useStore } from '../../../../store/useStore';
import { NodeHelpButton } from '../../../../components/NodeHelp';

type Clause = { op: string; value: string };

export const IfElseNode = ({ data, id }: any) => {
  const updateNodeData = useStore((s) => s.updateNodeData);
  const updateNodeInternals = useUpdateNodeInternals();
  const combinator: string = data.combinator || 'all';
  const clauses: Clause[] = data.clauses ?? [{ op: 'eq', value: '' }];

  // New clause handles must be re-measured to become connectable.
  useEffect(() => {
    updateNodeInternals(id);
  }, [id, clauses.length, updateNodeInternals]);

  const field =
    'nodrag min-w-0 bg-slate-50 dark:bg-slate-900 text-slate-800 dark:text-slate-100 border border-slate-200 dark:border-slate-700 rounded px-1 py-0.5 text-[10px] outline-none focus:ring-1 focus:ring-rose-500';
  const dot = 'w-3 h-3 bg-rose-500! border-2 border-white dark:border-slate-800!';

  const setClause = (index: number, patch: Partial<Clause>) =>
    updateNodeData(id, { clauses: clauses.map((c, i) => (i === index ? { ...c, ...patch } : c)) });
  const addClause = () => updateNodeData(id, { clauses: [...clauses, { op: 'eq', value: '' }] });
  const removeClause = (index: number) =>
    updateNodeData(id, { clauses: clauses.filter((_, i) => i !== index) });

  return (
    <div className="bg-white dark:bg-slate-800 border border-rose-500 rounded-lg shadow-md w-56 transition-colors">
      <div className="bg-rose-500 text-white px-2 py-1 font-semibold text-[11px] rounded-t-lg flex items-center gap-1">
        If / Else
        <NodeHelpButton id={id} type="ifElse" />
      </div>
      <div className="p-1.5 flex flex-col gap-1">
        {clauses.length > 1 && (
          <select
            className={field + ' w-full'}
            value={combinator}
            onChange={(e) => updateNodeData(id, { combinator: e.target.value })}
          >
            <option value="all">Match ALL (AND)</option>
            <option value="any">Match ANY (OR)</option>
          </select>
        )}

        {clauses.map((clause, index) => (
          <div key={index} className="relative flex items-center gap-1">
            <Handle type="target" position={Position.Left} id={`c${index}`} className={dot} />
            <span className="text-[9px] text-slate-400 w-3">{index + 1}</span>
            <select className={field + ' flex-1'} value={clause.op} onChange={(e) => setClause(index, { op: e.target.value })}>
              <option value="eq">=</option>
              <option value="neq">≠</option>
              <option value="gt">&gt;</option>
              <option value="gte">≥</option>
              <option value="lt">&lt;</option>
              <option value="lte">≤</option>
              <option value="contains">has</option>
            </select>
            <input className={field + ' flex-1'} placeholder="value" value={clause.value} onChange={(e) => setClause(index, { value: e.target.value })} />
            {clauses.length > 1 && (
              <button className="nodrag text-slate-300 hover:text-red-500" title="Remove condition" onClick={() => removeClause(index)}>
                <X size={11} />
              </button>
            )}
          </div>
        ))}
        <button className="nodrag flex items-center justify-center gap-1 text-[10px] text-rose-500 hover:text-rose-600" onClick={addClause}>
          <Plus size={11} /> Add condition
        </button>

        <div className="relative flex items-center gap-1 mt-0.5">
          <Handle type="target" position={Position.Left} id="then" className={dot} />
          <span className="text-[9px] uppercase tracking-wide text-emerald-500 w-7">then</span>
          <input className={field + ' flex-1'} placeholder="value or wire" value={data.then ?? ''} onChange={(e) => updateNodeData(id, { then: e.target.value })} />
        </div>
        <div className="relative flex items-center gap-1">
          <Handle type="target" position={Position.Left} id="else" className={dot} />
          <span className="text-[9px] uppercase tracking-wide text-slate-400 w-7">else</span>
          <input className={field + ' flex-1'} placeholder="value or wire" value={data.else ?? ''} onChange={(e) => updateNodeData(id, { else: e.target.value })} />
        </div>

      </div>
      <Handle type="source" position={Position.Right} id="out" className={dot} />
    </div>
  );
};
