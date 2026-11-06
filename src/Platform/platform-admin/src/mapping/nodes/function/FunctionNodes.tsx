import { useEffect, useMemo } from 'react';
import { Handle, Position, useUpdateNodeInternals } from '@xyflow/react';
import { useShallow } from 'zustand/react/shallow';
import { useStore } from '../../store/useStore';
import { TreeFields } from '../io/TreeFields';

const dot = 'w-3 h-3 border-2 border-white dark:border-slate-800!';

/** A function parameter (input). Editable name; its output feeds the body. */
export const ParamNode = ({ data, id }: any) => {
  const updateNodeData = useStore((s) => s.updateNodeData);
  return (
    <div className="bg-white dark:bg-slate-800 border border-amber-500 rounded-lg shadow-md w-40 transition-colors">
      <div className="bg-amber-500 text-white px-2 py-1 font-semibold text-[11px] rounded-t-lg">Parameter</div>
      <div className="p-2 relative flex items-center">
        <input
          className="nodrag flex-1 min-w-0 bg-slate-50 dark:bg-slate-900 text-slate-800 dark:text-slate-100 border border-slate-200 dark:border-slate-700 rounded px-1 py-0.5 text-[10px] outline-none focus:ring-1 focus:ring-amber-500"
          value={data.name ?? 'param'}
          onChange={(e) => updateNodeData(id, { name: e.target.value })}
        />
        <Handle type="source" position={Position.Right} id="out" className={`${dot} bg-amber-500!`} />
      </div>
    </div>
  );
};

/** The function's return value: one field → a scalar; several → an object. */
export const ReturnNode = ({ data, id }: any) => {
  const updateNodeData = useStore((s) => s.updateNodeData);
  const updateNodeInternals = useUpdateNodeInternals();
  const fields: string[] = data.fields ?? ['value'];
  const fieldsKey = fields.join('|');
  useEffect(() => {
    updateNodeInternals(id);
  }, [id, fieldsKey, updateNodeInternals]);

  return (
    <div className="bg-white dark:bg-slate-800 border border-emerald-500 rounded-lg shadow-md w-44 transition-colors">
      <div className="bg-emerald-500 text-white px-2 py-1 font-semibold text-[11px] rounded-t-lg">Return</div>
      <TreeFields
        fields={fields}
        onChange={(f) => updateNodeData(id, { fields: f })}
        side="target"
        inputClass="nodrag min-w-0 bg-slate-50 dark:bg-slate-900 text-slate-800 dark:text-slate-100 border border-slate-200 dark:border-slate-700 rounded px-1 py-0.5 text-[10px] outline-none focus:ring-1 focus:ring-emerald-500"
        dotClass={`${dot} bg-emerald-500!`}
        addClass="nodrag flex items-center justify-center gap-1 text-[10px] text-emerald-600 hover:text-emerald-700 mt-0.5"
      />
    </div>
  );
};

/** A call to a custom function: one input handle per parameter + an output. */
export const FunctionCallNode = ({ data, id }: any) => {
  const functionId: string = data.functionId;
  const updateNodeInternals = useUpdateNodeInternals();
  // Only primitives cross the useShallow boundary: an array built fresh by
  // .map() on every call would never compare equal by reference, so the
  // snapshot could never stabilize (React: "getSnapshot should be cached").
  const { name, paramsKey } = useStore(
    useShallow((s) => {
      const g = functionId === s.activeDocId ? { nodes: s.nodes } : s.inactiveDocs[functionId];
      const name = s.docs.find((d) => d.id === functionId)?.name ?? 'function';
      const paramsKey = (g?.nodes ?? [])
        .filter((n) => n.type === 'param')
        .map((n) => (n.data?.name as string) || 'param')
        .join('|');
      return { name, paramsKey };
    }),
  );
  const params = useMemo(() => (paramsKey ? paramsKey.split('|') : []), [paramsKey]);

  // New/removed parameters change the handles → re-measure so they connect.
  useEffect(() => {
    updateNodeInternals(id);
  }, [id, paramsKey, updateNodeInternals]);

  return (
    <div className="bg-white dark:bg-slate-800 border border-fuchsia-500 rounded-lg shadow-md w-44 transition-colors">
      <div className="bg-fuchsia-600 text-white px-2 py-1 font-semibold text-[11px] rounded-t-lg flex items-center gap-1">
        <span className="italic">ƒ</span> {name}
      </div>
      <div className="p-2 flex flex-col gap-1.5">
        {params.length === 0 && <div className="text-[9px] text-slate-400">no parameters</div>}
        {params.map((p) => (
          <div key={p} className="relative flex items-center text-[10px] text-slate-600 dark:text-slate-300">
            <Handle type="target" position={Position.Left} id={p} className={`${dot} bg-fuchsia-500!`} />
            {p}
          </div>
        ))}
      </div>
      <div className="relative h-3">
        <Handle type="source" position={Position.Right} id="out" className={`${dot} bg-fuchsia-500!`} style={{ top: -6 }} />
      </div>
    </div>
  );
};
