import { useEffect } from 'react';
import { useUpdateNodeInternals } from '@xyflow/react';
import { useStore } from '../../../store/useStore';
import { TreeFields } from '../TreeFields';

export const TargetNode = ({ data, id }: any) => {
  const updateNodeData = useStore((s) => s.updateNodeData);
  const updateNodeInternals = useUpdateNodeInternals();
  const fields: string[] = data.fields ?? ['totalAmount'];

  // Re-measure handles whenever fields change so new ones become connectable.
  const fieldsKey = fields.join('|');
  useEffect(() => {
    updateNodeInternals(id);
  }, [id, fieldsKey, updateNodeInternals]);

  return (
    <div className="bg-white dark:bg-slate-800 border border-purple-500 rounded-lg shadow-md w-52 transition-colors">
      <div className="bg-purple-500 text-white px-2 py-1 font-semibold text-[11px] rounded-t-lg flex items-center justify-between">
        <span>{data.label || 'Target Object'}</span>
        <button
          type="button"
          onClick={() => updateNodeData(id, { rootArray: !data.rootArray })}
          className="nodrag font-mono text-[10px] bg-white/20 hover:bg-white/30 rounded px-1 leading-tight transition-colors"
          title={
            data.rootArray
              ? 'Output is an array: click for a single object'
              : 'Output is a single object: click to output an array (one item per source list element)'
          }
        >
          {data.rootArray ? '[ ] array' : '{ } object'}
        </button>
      </div>
      <TreeFields
        fields={fields}
        types={data.types}
        rootArray={data.rootArray}
        onChange={(f) => updateNodeData(id, { fields: f })}
        side="target"
        inputClass="nodrag min-w-0 bg-slate-50 dark:bg-slate-900 text-slate-800 dark:text-slate-100 border border-slate-200 dark:border-slate-700 rounded px-1 py-0.5 text-[10px] outline-none focus:ring-1 focus:ring-purple-500"
        dotClass="w-3 h-3 bg-purple-500! border-2 border-white dark:border-slate-800!"
        addClass="nodrag flex items-center justify-center gap-1 text-[10px] text-purple-500 hover:text-purple-600 mt-0.5"
      />
    </div>
  );
};
