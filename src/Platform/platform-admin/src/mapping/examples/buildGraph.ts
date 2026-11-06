import type { Edge, Node } from '@xyflow/react';
import { inferStructure } from '../schema/inferFields';
import { ROOT_HANDLE } from '../nodes/io/fieldTree';
import { defaultNodeData } from '../nodes/registry';
import type { Recipe } from './recipes';

// Build a connected, typed Source -> (function nodes) -> Target graph for a
// recipe the generic decompiler can't faithfully rebuild. We construct the real
// intermediate nodes (Constant, Sort, GroupBy, Transform) so the example reads
// as an actual mapping, and it recompiles back to (an equivalent of) the DSL.

const strip = (p: string) => (p ?? '').replace(/^input\.?/, '').replace(/\[\]$/, '');
const isLiteral = (a: unknown) => typeof a === 'string' && a.length >= 2 && a.startsWith("'") && a.endsWith("'");
const unquote = (a: string) => a.slice(1, -1);

export function buildExampleGraph(r: Recipe): { nodes: Node[]; edges: Edge[] } {
  const s = inferStructure(r.input);
  const t = inferStructure(r.output);

  const nodes: Node[] = [];
  const edges: Edge[] = [];
  let seq = 0;
  let midY = 40;

  const source = {
    id: 'src',
    type: 'source',
    position: { x: 40, y: 40 },
    data: { label: 'Source', fields: s.fields.length ? s.fields : ['field'], lists: s.lists, types: s.types, rootArray: s.rootArray },
  } as Node;
  const target = {
    id: 'tgt',
    type: 'target',
    position: { x: 720, y: 40 },
    data: { label: 'Target', fields: t.fields.length ? t.fields : ['result'], lists: t.lists, types: t.types, rootArray: t.rootArray },
  } as Node;
  nodes.push(source, target);

  const addNode = (type: string, data: Record<string, unknown>) => {
    const id = `${type}-${++seq}`;
    nodes.push({ id, type, position: { x: 380, y: midY }, data: { ...defaultNodeData(type), ...data } } as Node);
    midY += 140;
    return id;
  };
  const wire = (source: string, sourceHandle: string, tgt: string, targetHandle: string) =>
    edges.push({ id: `e${++seq}`, source, sourceHandle, target: tgt, targetHandle } as Edge);

  // Build a function node (concat etc.) whose args reference item-relative fields
  // under `prefix` (or absolute when prefix is undefined). Returns the node id.
  const functionNode = (value: any, prefix: string | undefined) => {
    const args: any[] = value.args ?? [];
    const id = addNode('transform', { function: value.function, args: args.map((a) => ({ value: isLiteral(a) ? unquote(a) : '' })) });
    args.forEach((a, i) => {
      if (isLiteral(a) || typeof a !== 'string') return;
      const handle = prefix === undefined ? strip(a) : prefix ? `${prefix}.${a}` : a;
      wire('src', handle, id, `a${i}`);
    });
    return id;
  };

  // Wire a map value (string field or function) into a target handle.
  const wireValue = (value: any, prefix: string, targetHandle: string) => {
    if (typeof value === 'string') {
      wire('src', prefix ? `${prefix}.${value}` : value, 'tgt', targetHandle);
    } else if (value && typeof value === 'object' && value.function) {
      wire(functionNode(value, prefix), 'out', 'tgt', targetHandle);
    }
  };

  const dsl: any = r.dsl;
  if (dsl.target.type === 'array') {
    const prefix = strip(dsl.target.from);
    wire('src', prefix || ROOT_HANDLE, 'tgt', ROOT_HANDLE);
    for (const [k, v] of Object.entries(dsl.target.map ?? {})) wireValue(v, prefix, k);
    return { nodes, edges };
  }

  for (const rule of dsl.target.mappings ?? []) {
    const th = rule.target as string;
    switch (rule.type) {
      case 'fieldCopy':
        if (typeof rule.path === 'string' && rule.path.startsWith('input.')) {
          wire('src', strip(rule.path), 'tgt', th);
        } else {
          // A literal -> Constant node.
          wire(addNode('constant', { value: isLiteral(rule.path) ? unquote(rule.path) : String(rule.path) }), 'out', 'tgt', th);
        }
        break;
      case 'foreach': {
        const prefix = strip(rule.from);
        wire('src', prefix, 'tgt', th); // list -> list parent
        for (const [k, v] of Object.entries(rule.map ?? {})) wireValue(v, prefix, `${th}.${k}`);
        break;
      }
      case 'aggregate': {
        const id = addNode('aggregate', { function: rule.function, ...(rule.separator !== undefined ? { separator: rule.separator } : {}) });
        const field = rule.select ? `${strip(rule.from)}.${rule.select}` : strip(rule.from);
        if (field) wire('src', field, id, 'in');
        wire(id, 'out', 'tgt', th);
        break;
      }
      case 'sort': {
        const id = addNode('sort', { keys: (rule.keys ?? []).map((k: any) => ({ by: k.by ?? '', order: k.order ?? 'asc' })) });
        if (strip(rule.from)) wire('src', strip(rule.from), id, 'in');
        wire(id, 'out', 'tgt', th);
        break;
      }
      case 'filter': {
        const w = rule.where ?? {};
        const id = addNode('filter', { field: w.field ?? '', op: w.op ?? 'eq', value: w.value ?? '' });
        if (strip(rule.from)) wire('src', strip(rule.from), id, 'in');
        wire(id, 'out', 'tgt', th);
        break;
      }
      case 'groupBy': {
        const rows = Object.entries(rule.map ?? {}).map(([name, v]: [string, any]) =>
          v === 'key' ? { name, agg: 'key', field: '' } : { name, agg: v?.aggregate?.function ?? 'count', field: v?.aggregate?.select ?? '' },
        );
        const id = addNode('groupBy', { key: rule.key ?? '', rows });
        if (strip(rule.from)) wire('src', strip(rule.from), id, 'in');
        wire(id, 'out', 'tgt', th);
        break;
      }
      case 'transform':
        wire(functionNode(rule, undefined), 'out', 'tgt', th);
        break;
      // condition / join / valueMap / exception decompile cleanly elsewhere; if
      // we land here for them, leave the field unwired rather than guess.
      default:
        break;
    }
  }

  return { nodes, edges };
}
