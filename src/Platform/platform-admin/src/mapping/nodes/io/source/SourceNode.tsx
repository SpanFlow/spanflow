import { useEffect } from 'react';
import { useUpdateNodeInternals } from '@xyflow/react';
import { useStore } from '../../../store/useStore';
import { TreeFields } from '../TreeFields';

export const SourceNode = ({ data, id }: any) => {
  const updateNodeData = useStore((s) => s.updateNodeData);
  const updateNodeInternals = useUpdateNodeInternals();
  const fields: string[] = data.fields ?? [];

  // Re-measure handles whenever fields change so new ones become connectable.
  const fieldsKey = fields.join('|');
  useEffect(() => {
    updateNodeInternals(id);
  }, [id, fieldsKey, updateNodeInternals]);

  return (
    <div className="bg-white dark:bg-slate-800 border border-indigo-500 rounded-lg shadow-md w-52 transition-colors">
      <div className="bg-indigo-500 text-white px-2 py-1 font-semibold text-[11px] rounded-t-lg">
        {data.label || 'Input JSON'}
      </div>
      <TreeFields
        fields={fields}
        types={data.types}
        rootArray={data.rootArray}
        onChange={(f) => updateNodeData(id, { fields: f })}
        side="source"
        inputClass="nodrag min-w-0 bg-slate-50 dark:bg-slate-900 text-slate-800 dark:text-slate-100 border border-slate-200 dark:border-slate-700 rounded px-1 py-0.5 text-[10px] outline-none focus:ring-1 focus:ring-indigo-500"
        dotClass="w-3 h-3 bg-indigo-500! border-2 border-white dark:border-slate-800!"
        addClass="nodrag flex items-center justify-center gap-1 text-[10px] text-indigo-500 hover:text-indigo-600 mt-0.5"
      />
    </div>
  );
};
