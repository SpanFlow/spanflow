import { useState } from 'react';
import { Handle, Position } from '@xyflow/react';
import { Plus, X, ChevronRight, ChevronDown } from 'lucide-react';
import { buildFieldTree, renamePath, removePath, ROOT_HANDLE } from './fieldTree';

/** A name for a new field under `parentPath` (root when omitted) that doesn't
 *  collide with an existing sibling, so adding twice never silently merges. */
function nextFieldName(fields: string[], parentPath?: string): string {
  const prefix = parentPath ? `${parentPath}.` : '';
  const siblings = new Set(
    fields.filter((f) => f.startsWith(prefix)).map((f) => f.slice(prefix.length).split('.')[0]),
  );
  if (!siblings.has('new_field')) return 'new_field';
  let i = 2;
  while (siblings.has(`new_field_${i}`)) i += 1;
  return `new_field_${i}`;
}

interface Props {
  fields: string[];
  onChange: (fields: string[]) => void;
  side: 'source' | 'target';
  inputClass: string;
  dotClass: string;
  addClass: string;
  types?: Record<string, string>;
  rootArray?: boolean; // a top-level array target — show a connectable array-root parent
}

const TYPE_STYLES: Record<string, string> = {
  string: 'text-emerald-600 dark:text-emerald-400',
  number: 'text-blue-600 dark:text-blue-400',
  boolean: 'text-amber-600 dark:text-amber-400',
  null: 'text-slate-400',
  object: 'text-slate-500 dark:text-slate-400',
  array: 'text-purple-600 dark:text-purple-400',
};

const TYPE_LABEL: Record<string, string> = {
  string: 'str',
  number: 'num',
  boolean: 'bool',
  null: 'null',
  object: '{ }',
  array: '[ ]',
};

const TypeBadge = ({ type }: { type?: string }) =>
  type ? (
    <span className={`font-mono text-[9px] shrink-0 ${TYPE_STYLES[type] ?? 'text-slate-400'}`} title={type}>
      {TYPE_LABEL[type] ?? type}
    </span>
  ) : null;

/** Renders dotted field paths as an indented tree with a handle per connectable node. */
export const TreeFields = ({ fields, onChange, side, inputClass, dotClass, addClass, types, rootArray }: Props) => {
  const tree = buildFieldTree(fields);
  const handleType = side === 'source' ? 'source' : 'target';
  const handlePos = side === 'source' ? Position.Right : Position.Left;

  // For an array-root target, indent the element fields under a parent "array" row.
  const indent = rootArray ? 1 : 0;
  const typeOf = (node: { path: string; hasChildren: boolean }) =>
    types?.[node.path] ?? (node.hasChildren ? 'object' : undefined);

  const [collapsed, setCollapsed] = useState<Set<string>>(new Set());
  const toggle = (path: string) =>
    setCollapsed((prev) => {
      const next = new Set(prev);
      if (!next.delete(path)) next.add(path);
      return next;
    });
  const hidden = (path: string) => [...collapsed].some((c) => path.startsWith(`${c}.`));

  const rename = (path: string, segment: string) => onChange(renamePath(fields, path, segment));
  const remove = (path: string) => onChange(removePath(fields, path));

  // Adding under a parent (an object/array node) targets that node directly,
  // instead of relying on someone typing a dotted path at the root — typing a
  // name that happens to match an existing parent just merges into it and the
  // new row silently disappears.
  const addField = (parentPath?: string) => {
    const name = nextFieldName(fields, parentPath);
    const newPath = parentPath ? `${parentPath}.${name}` : name;
    onChange([...fields, newPath]);
    if (parentPath) {
      setCollapsed((prev) => {
        if (!prev.has(parentPath)) return prev;
        const next = new Set(prev);
        next.delete(parentPath);
        return next;
      });
    }
  };

  return (
    <div className="p-1.5 flex flex-col gap-0.5">
      {rootArray && (
        <div className="relative flex items-center gap-1">
          <Handle type={handleType} position={handlePos} id={ROOT_HANDLE} className={dotClass} />
          <span className="w-[11px] shrink-0" />
          <span className="flex-1 text-[10px] italic text-slate-500 dark:text-slate-400">array of</span>
          <TypeBadge type="array" />
        </div>
      )}
      {tree.filter((node) => !hidden(node.path)).map((node, i) => (
        <div
          // Keyed by position, not path: the path changes as the field name is
          // typed, and a changing key would remount the input and drop focus.
          key={i}
          className="relative flex items-center gap-1"
          style={{ paddingLeft: (node.depth + indent) * 12 }}
        >
          {side === 'target' && node.connectable && (
            <Handle type={handleType} position={handlePos} id={node.path} className={dotClass} />
          )}
          {node.hasChildren ? (
            <button
              className="nodrag text-slate-400 hover:text-slate-600 dark:hover:text-slate-200 shrink-0"
              title={collapsed.has(node.path) ? 'Expand' : 'Collapse'}
              onClick={() => toggle(node.path)}
            >
              {collapsed.has(node.path) ? <ChevronRight size={11} /> : <ChevronDown size={11} />}
            </button>
          ) : (
            <span className="w-[11px] shrink-0" />
          )}
          <input
            className={inputClass + ' flex-1'}
            value={node.segment}
            onChange={(e) => rename(node.path, e.target.value)}
          />
          <TypeBadge type={typeOf(node)} />
          {node.hasChildren && (
            <button
              className="nodrag text-slate-300 hover:text-emerald-500 transition-colors"
              title={`Add field to ${node.segment}`}
              onClick={() => addField(node.path)}
            >
              <Plus size={11} />
            </button>
          )}
          <button
            className="nodrag text-slate-300 hover:text-red-500 transition-colors"
            title="Remove"
            onClick={() => remove(node.path)}
          >
            <X size={11} />
          </button>
          {side === 'source' && node.connectable && (
            <Handle type={handleType} position={handlePos} id={node.path} className={dotClass} />
          )}
        </div>
      ))}
      <button className={addClass} onClick={() => addField()}>
        <Plus size={11} /> Add field
      </button>
    </div>
  );
};
