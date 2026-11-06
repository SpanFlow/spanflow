import type { Edge, Node } from '@xyflow/react';
import { defaultNodeData } from './registry';

// Reverse of the compiler: turn a mapping DSL (typed or legacy) back into a
// node graph. Handles the shapes the designer emits; nested inputs are rebuilt
// recursively. Layout is depth-based (columns) — good enough to read and edit.

type Ref = { id: string; handle: string; depth: number };

interface Pair { key: string; value: string }

const COL_W = 220;
const ROW_H = 130;

/** Strip `input.` and a trailing `[]` from a path to get a source field name. */
function sourceField(path: string): string | null {
  if (typeof path !== 'string' || !path.startsWith('input.')) return null;
  let f = path.slice('input.'.length);
  if (f.endsWith('[]')) f = f.slice(0, -2);
  return f;
}

/** Turn an engine WhereDefinition back into Where node data (match + clauses). */
function whereData(clause: any): { match: 'all' | 'any'; clauses: { field: string; op: string; value: string }[] } {
  const leaf = (c: any) => ({ field: String(c?.field ?? ''), op: String(c?.op ?? 'eq'), value: String(c?.value ?? '') });
  const match: 'all' | 'any' = Array.isArray(clause?.any) ? 'any' : 'all';
  const group = clause?.all ?? clause?.any;
  return { match, clauses: Array.isArray(group) ? group.map(leaf) : [leaf(clause)] };
}

/** Unwrap a quoted literal like `'Win'` -> `Win`; otherwise stringify. */
function literalText(v: unknown): string {
  if (typeof v === 'string') {
    return v.length >= 2 && v[0] === "'" && v.endsWith("'") ? v.slice(1, -1) : v;
  }
  return String(v);
}

class Builder {
  nodes: Node[] = [];
  edges: Edge[] = [];
  private seq = 0;
  private colY: Record<number, number> = {};
  private sourceId = 'src';
  private sourceFields: string[] = [];
  private targetId = 'tgt';
  private targetFields: string[] = [];
  private maxDepth = 0;

  private id(prefix: string) {
    return `${prefix}-${++this.seq}`;
  }

  private place(depth: number): { x: number; y: number } {
    this.maxDepth = Math.max(this.maxDepth, depth);
    const y = this.colY[depth] ?? 40;
    this.colY[depth] = y + ROW_H;
    return { x: 40 + depth * COL_W, y };
  }

  private addNode(type: string, depth: number, data: Record<string, unknown>): string {
    const id = this.id(type);
    this.nodes.push({ id, type, position: this.place(depth), data: { ...defaultNodeData(type), ...data } } as Node);
    return id;
  }

  private connect(source: Ref, targetId: string, targetHandle: string) {
    this.edges.push({
      id: `e-${++this.seq}`,
      source: source.id,
      sourceHandle: source.handle,
      target: targetId,
      targetHandle,
    } as Edge);
  }

  private ensureSourceField(field: string) {
    if (!this.sourceFields.includes(field)) this.sourceFields.push(field);
  }

  /** Build the node(s) for an expression and return where to wire from. */
  private materialize(expr: any): Ref | null {
    if (expr == null) return null;

    if (typeof expr === 'string') {
      const field = sourceField(expr);
      if (field == null) return null; // literal / unsupported
      this.ensureSourceField(field);
      return { id: this.sourceId, handle: field, depth: 0 };
    }
    if (typeof expr !== 'object') return null;

    if (expr.aggregate) return this.aggregate(expr.aggregate);
    if (expr.valueMap) return this.valueMap(expr.valueMap);
    if (expr.sort) return this.sort(expr.sort);
    if (expr.filter) return this.filter(expr.filter);
    if (expr.transform) return this.transform(expr.transform);
    if (expr.condition) return this.condition(expr.condition);
    if (expr.join) return this.join(expr.join);
    if (expr.exception) return this.exception(expr.exception);
    return null;
  }

  // ---- per-node builders (return the node's output ref) -------------------

  private aggregate(def: any): Ref {
    const coll = sourceField(def.from ?? '') ?? '';
    const field = def.select ? `${coll}.${def.select}` : coll;
    let inputDepth = 0;
    if (field) this.ensureSourceField(field);
    const data: Record<string, unknown> = { function: def.function };
    if (def.separator !== undefined) data.separator = def.separator;
    const id = this.addNode('aggregate', inputDepth + 1, data);
    if (field) this.connect({ id: this.sourceId, handle: field, depth: 0 }, id, 'in');
    return { id, handle: 'out', depth: 1 };
  }

  private collectionInput(def: any, nodeId: string): number {
    // Wire a node's single list input from `from` (source path) or `input` (nested).
    if (def.input !== undefined) {
      const ref = this.materialize(def.input);
      if (ref) {
        this.connect(ref, nodeId, 'in');
        return ref.depth;
      }
    }
    const field = sourceField(def.from ?? '');
    if (field != null) {
      this.ensureSourceField(field);
      this.connect({ id: this.sourceId, handle: field, depth: 0 }, nodeId, 'in');
    }
    return 0;
  }

  private valueMap(def: any): Ref {
    const pairs: Pair[] = Object.entries(def.table ?? {}).map(([key, value]) => ({ key, value: String(value) }));
    const data: Record<string, unknown> = { pairs };
    if (def.default !== undefined) data.default = def.default;
    const id = this.addNode('valueMap', 1, data);
    const d = this.collectionInput(def, id);
    this.reposition(id, d + 1);
    return { id, handle: 'out', depth: d + 1 };
  }

  private sort(def: any): Ref {
    const keys = (def.keys ?? []).map((k: any) => ({ by: k.by ?? '', order: k.order ?? 'asc' }));
    const id = this.addNode('sort', 1, { keys });
    const d = this.collectionInput(def, id);
    this.reposition(id, d + 1);
    return { id, handle: 'out', depth: d + 1 };
  }

  private filter(def: any): Ref {
    const where = def.where ?? {};
    const id = this.addNode('filter', 1, { field: where.field ?? '', op: where.op ?? 'eq', value: where.value ?? '' });
    const d = this.collectionInput(def, id);
    this.reposition(id, d + 1);
    return { id, handle: 'out', depth: d + 1 };
  }

  private transform(def: any): Ref {
    const args = (def.args ?? []).map(() => ({ value: '' }));
    const id = this.addNode('transform', 1, { function: def.function, args });
    let maxDepth = 0;
    (def.args ?? []).forEach((arg: any, i: number) => {
      const ref = typeof arg === 'string' && sourceField(arg) == null ? null : this.materialize(arg);
      if (ref) {
        this.connect(ref, id, `a${i}`);
        maxDepth = Math.max(maxDepth, ref.depth);
      } else {
        // literal argument; a null one ("not given") stays blank rather than
        // becoming the text 'null', which the engine would read as a value.
        (args[i] as Pair).value = arg == null ? '' : (literalText(arg) as unknown as string);
      }
    });
    this.reposition(id, maxDepth + 1);
    return { id, handle: 'out', depth: maxDepth + 1 };
  }

  private condition(def: any): Ref {
    const ifExpr = def.if ?? {};
    const combinator = ifExpr.all ? 'all' : ifExpr.any ? 'any' : 'all';
    const leaves: any[] = ifExpr.all ?? ifExpr.any ?? [ifExpr];
    const clauses = leaves.map((c) => ({ op: c.op ?? 'eq', value: c.value ?? '' }));
    const id = this.addNode('condition', 1, {
      combinator,
      clauses,
      then: literalText(def.then),
      else: literalText(def.else),
    });

    let maxDepth = 0;
    leaves.forEach((c, i) => {
      const ref = this.materialize(c.input);
      if (ref) {
        this.connect(ref, id, `c${i}`);
        maxDepth = Math.max(maxDepth, ref.depth);
      }
    });
    // then/else may be wired nodes (objects), otherwise they stay literals.
    for (const branch of ['then', 'else'] as const) {
      if (def[branch] && typeof def[branch] === 'object') {
        const ref = this.materialize(def[branch]);
        if (ref) {
          this.connect(ref, id, branch);
          maxDepth = Math.max(maxDepth, ref.depth);
        }
      }
    }
    this.reposition(id, maxDepth + 1);
    return { id, handle: 'out', depth: maxDepth + 1 };
  }

  private join(def: any): Ref {
    const id = this.addNode('join', 1, { leftKey: def.leftKey ?? '', rightKey: def.rightKey ?? '' });
    let maxDepth = 0;
    const side = (pathKey: string, inputKey: string, handle: string) => {
      if (def[inputKey] !== undefined) {
        const ref = this.materialize(def[inputKey]);
        if (ref) { this.connect(ref, id, handle); maxDepth = Math.max(maxDepth, ref.depth); }
      } else {
        const field = sourceField(def[pathKey] ?? '');
        if (field != null) { this.ensureSourceField(field); this.connect({ id: this.sourceId, handle: field, depth: 0 }, id, handle); }
      }
    };
    side('left', 'leftInput', 'left');
    side('right', 'rightInput', 'right');
    this.reposition(id, maxDepth + 1);
    return { id, handle: 'out', depth: maxDepth + 1 };
  }

  private foreach(target: string, from: string, map: any): Ref {
    // Only simple field projections decompile into a foreach node; per-item
    // function expressions can't be represented here, so skip them.
    const rows = Object.entries(map ?? {})
      .filter(([, f]) => typeof f === 'string')
      .map(([name, f]) => ({ name, from: f === name ? '' : String(f) }));
    const id = this.addNode('foreach', 1, { rows });
    const field = sourceField(from);
    if (field != null) { this.ensureSourceField(field); this.connect({ id: this.sourceId, handle: field, depth: 0 }, id, 'in'); }
    void target;
    return { id, handle: 'out', depth: 1 };
  }

  private groupBy(def: any): Ref {
    const rows = Object.entries(def.map ?? {}).map(([name, v]: [string, any]) => {
      if (v === 'key') return { name, agg: 'key', field: '' };
      const agg = v?.aggregate ?? {};
      return { name, agg: agg.function ?? 'sum', field: agg.select ?? '' };
    });
    const id = this.addNode('groupBy', 1, { key: def.key ?? '', rows });
    const field = sourceField(def.from ?? '');
    if (field != null) { this.ensureSourceField(field); this.connect({ id: this.sourceId, handle: field, depth: 0 }, id, 'in'); }

    // Each optional filter comes back as its own Where node wired to that port.
    for (const port of ['where', 'having'] as const) {
      const clause = def[port];
      if (!clause) continue;
      const whereId = this.addNode('where', 0, whereData(clause));
      this.connect({ id: whereId, handle: 'out', depth: 0 }, id, port);
    }

    return { id, handle: 'out', depth: 1 };
  }

  private exception(def: any): Ref {
    const id = this.addNode('exception', 1, {
      op: def.op ?? 'eq',
      value: def.value ?? '',
      message: def.message ?? '',
    });
    const d = this.collectionInput(def, id);
    this.reposition(id, d + 1);
    return { id, handle: 'out', depth: d + 1 };
  }

  private reposition(id: string, depth: number) {
    const node = this.nodes.find((n) => n.id === id);
    if (node) node.position = this.place(depth);
  }

  /** Build a top-level rule and wire its output to the target field. */
  buildRule(rule: any) {
    const target = rule.target as string;
    if (!target) return;
    this.targetFields.push(target);

    const norm = normalizeRule(rule);
    if (!norm) return;
    const { mode, def } = norm;

    let ref: Ref | null = null;
    switch (mode) {
      case 'fieldCopy': {
        const field = sourceField(def.path ?? '');
        if (field != null) { this.ensureSourceField(field); ref = { id: this.sourceId, handle: field, depth: 0 }; }
        break;
      }
      case 'aggregate': ref = this.aggregate(def); break;
      case 'valueMap': ref = this.valueMap(def); break;
      case 'sort': ref = this.sort(def); break;
      case 'filter': ref = this.filter(def); break;
      case 'transform': ref = this.transform(def); break;
      case 'condition': ref = this.condition(def); break;
      case 'join': ref = this.join(def); break;
      case 'foreach': ref = this.foreach(target, def.from, def.map); break;
      case 'groupBy': ref = this.groupBy(def); break;
      case 'exception': ref = this.exception(def); break;
    }
    if (ref) this.connect(ref, this.targetId, target);
  }

  finish() {
    // Source on the far left, target on the far right.
    this.nodes.unshift({
      id: this.sourceId,
      type: 'source',
      position: { x: 40, y: 40 },
      data: { label: 'Source', fields: this.sourceFields.length ? this.sourceFields : ['field'] },
    } as Node);
    this.nodes.push({
      id: this.targetId,
      type: 'target',
      position: { x: 40 + (this.maxDepth + 1) * COL_W, y: 40 },
      data: { label: 'Target', fields: this.targetFields.length ? this.targetFields : ['result'] },
    } as Node);
  }
}

/** Normalize a typed or legacy rule into { mode, def }. */
function normalizeRule(rule: any): { mode: string; def: any } | null {
  if (rule.type) {
    const { type, target, ...rest } = rule;
    void target;
    const mode = String(type).toLowerCase() === 'ifelse' ? 'condition'
      : String(type).toLowerCase() === 'field' ? 'fieldCopy'
      : type;
    return { mode, def: rest };
  }
  if (rule.from !== undefined) return { mode: 'fieldCopy', def: { path: rule.from } };
  if (rule.foreach !== undefined) return { mode: 'foreach', def: { from: rule.foreach, map: rule.map } };
  for (const key of ['aggregate', 'valueMap', 'sort', 'filter', 'transform', 'condition', 'join', 'groupBy', 'exception'] as const) {
    if (rule[key]) return { mode: key, def: rule[key] };
  }
  return null;
}

export function dslToGraph(dsl: any): { nodes: Node[]; edges: Edge[] } {
  const builder = new Builder();
  for (const rule of dsl?.target?.mappings ?? []) builder.buildRule(rule);
  builder.finish();
  return { nodes: builder.nodes, edges: builder.edges };
}
