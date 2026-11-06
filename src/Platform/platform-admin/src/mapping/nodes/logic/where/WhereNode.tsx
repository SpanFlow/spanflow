import { Handle, Position } from '@xyflow/react';
import { Plus, X } from 'lucide-react';
import { useStore } from '../../../store/useStore';
import { NodeHelpButton } from '../../../components/NodeHelp';

type Clause = { field: string; op: string; value: string };

const OPS: [string, string][] = [
  ['eq', '='],
  ['neq', '≠'],
  ['gt', '>'],
  ['gte', '≥'],
  ['lt', '<'],
  ['lte', '≤'],
  ['contains', 'has'],
];

export const WhereNode = ({ data, id }: any) => {
  const updateNodeData = useStore((s) => s.updateNodeData);
  const clauses: Clause[] = data.clauses ?? [{ field: '', op: 'eq', value: '' }];
  const match: 'all' | 'any' = data.match === 'any' ? 'any' : 'all';
  const field =
    'nodrag min-w-0 bg-slate-50 dark:bg-slate-900 text-slate-800 dark:text-slate-100 border border-slate-200 dark:border-slate-700 rounded px-1 py-0.5 text-[10px] outline-none focus:ring-1 focus:ring-amber-500';

  const setClause = (index: number, patch: Partial<Clause>) =>
    updateNodeData(id, { clauses: clauses.map((c, i) => (i === index ? { ...c, ...patch } : c)) });
  const addClause = () => updateNodeData(id, { clauses: [...clauses, { field: '', op: 'eq', value: '' }] });
  const removeClause = (index: number) => updateNodeData(id, { clauses: clauses.filter((_, i) => i !== index) });

  return (
    <div className="bg-white dark:bg-slate-800 border border-amber-500 rounded-lg shadow-md w-64 transition-colors relative">
      <div className="bg-amber-500 text-white px-2 py-1 font-semibold text-[11px] rounded-t-lg flex items-center gap-1">
        Where
        <NodeHelpButton id={id} type="where" />
      </div>
      <div className="p-1.5 flex flex-col gap-1">
        <div className="flex items-center gap-1">
          <span className="text-[9px] uppercase tracking-wide text-slate-400">match</span>
          <select
            className={field}
            value={match}
            onChange={(e) => updateNodeData(id, { match: e.target.value })}
          >
            <option value="all">all (AND)</option>
            <option value="any">any (OR)</option>
          </select>
          <span className="text-[9px] text-slate-400">of the rules below</span>
        </div>

        {clauses.map((clause, index) => (
          <div key={index} className="flex items-center gap-1">
            <input
              className={field + ' flex-1'}
              placeholder="field"
              value={clause.field}
              onChange={(e) => setClause(index, { field: e.target.value })}
            />
            <select className={field} value={clause.op} onChange={(e) => setClause(index, { op: e.target.value })}>
              {OPS.map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
            <input
              className={field + ' flex-1'}
              placeholder="value"
              value={clause.value}
              onChange={(e) => setClause(index, { value: e.target.value })}
            />
            {clauses.length > 1 && (
              <button className="nodrag text-slate-300 hover:text-red-500" title="Remove rule" onClick={() => removeClause(index)}>
                <X size={11} />
              </button>
            )}
          </div>
        ))}

        <button className="nodrag flex items-center justify-center gap-1 text-[10px] text-amber-600 hover:text-amber-700" onClick={addClause}>
          <Plus size={11} /> Add rule
        </button>
      </div>
      <Handle type="source" position={Position.Right} id="out" className="w-3 h-3 bg-amber-500! border-2 border-white dark:border-slate-800!" />
    </div>
  );
};
