import { useEffect } from 'react';
import { Handle, Position, useUpdateNodeInternals } from '@xyflow/react';
import { Plus, X } from 'lucide-react';
import { useStore } from '../../../store/useStore';
import { TRANSFORMS, TRANSFORM_GROUPS, transformInfo } from './transforms';
import { NodeHelpButton } from '../../../components/NodeHelp';

type Arg = { value: string };

export const TransformNode = ({ data, id }: any) => {
  const updateNodeData = useStore((s) => s.updateNodeData);
  const updateNodeInternals = useUpdateNodeInternals();
  const fn = data.function || 'concat';
  const args: Arg[] = data.args ?? [{ value: '' }, { value: '' }];

  // New arg handles must be re-measured to become connectable.
  useEffect(() => {
    updateNodeInternals(id);
  }, [id, args.length, updateNodeInternals]);

  const field =
    'nodrag min-w-0 bg-slate-50 dark:bg-slate-900 text-slate-800 dark:text-slate-100 border border-slate-200 dark:border-slate-700 rounded px-1 py-0.5 text-[10px] outline-none focus:ring-1 focus:ring-cyan-500';
  const dot = 'w-3 h-3 bg-cyan-500! border-2 border-white dark:border-slate-800!';

  const setArg = (index: number, value: string) =>
    updateNodeData(id, { args: args.map((a, i) => (i === index ? { ...a, value } : a)) });
  const addArg = () => updateNodeData(id, { args: [...args, { value: '' }] });
  const removeArg = (index: number) => updateNodeData(id, { args: args.filter((_, i) => i !== index) });

  const params = transformInfo(fn)?.params ?? [];
  // Picking a function gives it a slot per argument it takes. Existing args are
  // kept (and nothing is dropped), so wires survive switching function.
  const pickFunction = (next: string) => {
    const wanted = transformInfo(next)?.params?.length ?? 0;
    const padded = args.length < wanted ? [...args, ...Array.from({ length: wanted - args.length }, () => ({ value: '' }))] : args;
    updateNodeData(id, { function: next, args: padded });
  };

  return (
    <div className="bg-white dark:bg-slate-800 border border-cyan-500 rounded-lg shadow-md w-52 transition-colors relative">
      <div className="bg-cyan-500 text-white px-2 py-1 font-semibold text-[11px] rounded-t-lg flex items-center gap-1">
        Transform
        <NodeHelpButton id={id} type="transform" />
      </div>
      <div className="p-1.5 flex flex-col gap-1">
        <select
          className={field + ' w-full'}
          value={fn}
          onChange={(e) => pickFunction(e.target.value)}
        >
          {/* A pasted mapping may name a function this list doesn't know yet; show it as-is. */}
          {!transformInfo(fn) && <option value={fn}>{fn}</option>}
          {TRANSFORM_GROUPS.map((group) => (
            <optgroup key={group} label={group}>
              {TRANSFORMS.filter((t) => t.group === group).map((t) => (
                <option key={t.value} value={t.value}>{t.label}</option>
              ))}
            </optgroup>
          ))}
        </select>

        {args.map((arg, index) => (
          <div key={index} className="relative flex items-center gap-1">
            <Handle type="target" position={Position.Left} id={`a${index}`} className={dot} />
            <span className="text-[9px] text-slate-400 w-3">{index + 1}</span>
            <input
              className={field + ' flex-1'}
              placeholder={params[index] ?? 'value or wire'}
              title={params[index]}
              value={arg.value}
              onChange={(e) => setArg(index, e.target.value)}
            />
            {args.length > 1 && (
              <button className="nodrag text-slate-300 hover:text-red-500" title="Remove argument" onClick={() => removeArg(index)}>
                <X size={11} />
              </button>
            )}
          </div>
        ))}
        <button className="nodrag flex items-center justify-center gap-1 text-[10px] text-cyan-600 hover:text-cyan-700" onClick={addArg}>
          <Plus size={11} /> Add argument
        </button>

      </div>
      <Handle type="source" position={Position.Right} id="out" className={dot} />
    </div>
  );
};
