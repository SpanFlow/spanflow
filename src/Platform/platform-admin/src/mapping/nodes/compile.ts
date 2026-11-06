import type { Edge, Node } from '@xyflow/react';
import type { CompileContext, Expr } from './compile-types';
import { specByType } from './registry';
import { ROOT_HANDLE } from './io/fieldTree';

export interface MappingDSL {
  target: {
    type: string;
    mappings?: any[];
    // Array-root target: project a source list into a list output.
    from?: string;
    map?: Record<string, unknown>;
  };
}

/** A reusable custom function: its own graph, keyed by document id. */
export interface FunctionDef {
  id: string;
  name: string;
  params: string[];
  nodes: Node[];
  edges: Edge[];
}
export type FunctionMap = Record<string, FunctionDef>;

/**
 * The value a function returns: its Return node's fields. A single field yields
 * a scalar; multiple fields yield an object-construction ({ object: {...} }).
 */
function buildReturnExpr(nodes: Node[], edges: Edge[], functions: FunctionMap, stack: string[]): Expr {
  const ret = nodes.find((n) => n.type === 'return');
  if (!ret) return null;
  const fields = ((ret.data?.fields as string[] | undefined) ?? ['value']).filter(Boolean);

  const exprFor = (handle: string): Expr => {
    const e = edges.find((x) => x.target === ret.id && x.targetHandle === handle);
    const feeder = e && nodes.find((n) => n.id === e.source);
    return feeder ? buildExpr(feeder, e!.sourceHandle, nodes, edges, functions, stack) : null;
  };

  if (fields.length <= 1) return exprFor(fields[0] ?? 'value');
  const object: Record<string, unknown> = {};
  for (const f of fields) object[f] = exprFor(f);
  return { object };
}

/** Replace {__param:name} placeholders with the caller's argument expressions. */
function substituteParams(expr: Expr, args: Record<string, Expr>): Expr {
  if (Array.isArray(expr)) return expr.map((e) => substituteParams(e as Expr, args)) as unknown as Expr;
  if (expr && typeof expr === 'object') {
    const o = expr as Record<string, unknown>;
    if ('__param' in o) return args[o.__param as string] ?? null;
    const out: Record<string, unknown> = {};
    for (const [k, v] of Object.entries(o)) out[k] = substituteParams(v as Expr, args);
    return out as Expr;
  }
  return expr;
}

/** Inline a function-call node: compile the function's return, then substitute
 *  each parameter with the expression wired to the call node in the caller. */
function inlineFunctionCall(
  callNode: Node,
  nodes: Node[],
  edges: Edge[],
  functions: FunctionMap,
  stack: string[],
): Expr {
  const fnId = callNode.data?.functionId as string | undefined;
  const fn = fnId ? functions[fnId] : undefined;
  if (!fn || stack.includes(fn.id)) return null; // missing or recursive → bail

  const body = buildReturnExpr(fn.nodes, fn.edges, functions, [...stack, fn.id]);

  const args: Record<string, Expr> = {};
  for (const p of fn.params) {
    const e = edges.find((x) => x.target === callNode.id && x.targetHandle === p);
    const up = e && nodes.find((n) => n.id === e.source);
    args[p] = up ? buildExpr(up, e!.sourceHandle, nodes, edges, functions, stack) : null;
  }
  return substituteParams(body, args);
}

/** Compile a node's output expression by delegating to its spec. */
function buildExpr(
  node: Node,
  outHandle: string | null | undefined,
  nodes: Node[],
  edges: Edge[],
  functions: FunctionMap = {},
  stack: string[] = [],
): Expr {
  // A Parameter node is a placeholder; a function call inlines its body.
  if (node.type === 'param') return { __param: (node.data?.name as string) || 'param' };
  if (node.type === 'functionCall') return inlineFunctionCall(node, nodes, edges, functions, stack);

  const spec = specByType[node.type as string];
  if (!spec) return null;

  const ctx: CompileContext = {
    node,
    outHandle,
    nodes,
    edges,
    build: (n, h) => buildExpr(n, h, nodes, edges, functions, stack),
    incomingEdge: (targetHandle) =>
      edges.find((e) => e.target === node.id && (targetHandle === undefined || e.targetHandle === targetHandle)),
    inputExpr: (targetHandle) => {
      const edge = edges.find((e) => e.target === node.id && e.targetHandle === targetHandle);
      if (!edge) return undefined;
      const upstream = nodes.find((n) => n.id === edge.source);
      return upstream ? buildExpr(upstream, edge.sourceHandle, nodes, edges, functions, stack) : undefined;
    },
  };

  return spec.compile(ctx);
}

/**
 * Lift a nested expression into a top-level mapping rule. The engine expects the
 * execution mode (aggregate/valueMap/sort/...) at the rule's top level, and a
 * bare path string maps to `from`.
 */
function exprToRule(target: string, expr: Expr): Record<string, unknown> | Record<string, unknown>[] | null {
  if (expr == null) return null;
  // A bare path is a field copy.
  if (typeof expr === 'string') return { target, type: 'fieldCopy', path: expr };
  // Numeric/boolean literal (e.g. a Constant node) — coalesce yields the value.
  if (typeof expr === 'number' || typeof expr === 'boolean') {
    return { target, type: 'transform', function: 'coalesce', args: [expr] };
  }
  // An object return: spread into one rule per nested field (target.field).
  if ((expr as any).object) {
    const rules: Record<string, unknown>[] = [];
    for (const [k, v] of Object.entries((expr as any).object)) {
      const sub = exprToRule(`${target}.${k}`, v as Expr);
      if (Array.isArray(sub)) rules.push(...sub);
      else if (sub) rules.push(sub);
    }
    return rules;
  }
  // Each node produces a single nested mode; lift it into the typed, flattened rule.
  if (expr.aggregate) return { target, type: 'aggregate', ...expr.aggregate };
  if (expr.valueMap) return { target, type: 'valueMap', ...expr.valueMap };
  if (expr.sort) return { target, type: 'sort', ...expr.sort };
  if (expr.filter) return { target, type: 'filter', ...expr.filter };
  if (expr.foreach !== undefined) return { target, type: 'foreach', from: expr.foreach, map: expr.map };
  if (expr.foreachInput !== undefined) return { target, type: 'foreach', input: expr.foreachInput, map: expr.map };
  if (expr.groupBy) return { target, type: 'groupBy', ...expr.groupBy };
  if (expr.join) return { target, type: 'join', ...expr.join };
  if (expr.transform) return { target, type: 'transform', ...expr.transform };
  if (expr.condition) return { target, type: 'condition', ...expr.condition };
  if (expr.exception) return { target, type: 'exception', ...expr.exception };
  return null;
}

/**
 * Where a foreach iterates: `from` is the engine path (e.g. `input.person[]` or
 * `input[]` for a root-array source); `prefix` is the source-handle prefix that
 * identifies an item field ('' for a root array).
 */
interface SourceIter {
  from: string;
  prefix: string;
}

/** The handle path of a source field made relative to the iterated item, or null if not an item field. */
function relHandle(sh: string, prefix: string): string | null {
  if (prefix === '') return sh || null;
  if (sh.startsWith(`${prefix}.`)) return sh.slice(prefix.length + 1);
  return null;
}

/**
 * Rewrite paths that point into the iterated source so they are relative to the
 * current item (e.g. `input.person.fullName` -> `fullName`, or `input.id` -> `id`
 * for a root array), so a function can be evaluated per-item. Recurses.
 */
function relativize(expr: Expr, from: string): Expr {
  const base = from.replace(/\[\]$/, ''); // `input.person` or `input`
  if (typeof expr === 'string') {
    const collPrefix = `${base}[].`;
    const dotPrefix = `${base}.`;
    if (expr.startsWith(collPrefix)) return expr.slice(collPrefix.length);
    if (expr.startsWith(dotPrefix)) return expr.slice(dotPrefix.length);
    if (expr === base || expr === `${base}[]`) return '$';
    return expr;
  }
  if (Array.isArray(expr)) return expr.map((e) => relativize(e as Expr, from));
  if (expr && typeof expr === 'object') {
    const out: Record<string, unknown> = {};
    for (const [k, v] of Object.entries(expr)) out[k] = relativize(v as Expr, from);
    return out as Expr;
  }
  return expr;
}

/** Convert a buildExpr result into the inline form a foreach map value expects. */
function toInlineExpr(expr: Expr): unknown {
  if (expr && typeof expr === 'object') {
    const o = expr as Record<string, unknown>;
    if (o.transform) return o.transform; // engine recognizes the "function" key
    if (o.condition) return o.condition; // and the "if" key
  }
  return expr; // aggregate/valueMap/sort/filter/join stay wrapped; strings pass through
}

/**
 * Build a foreach `map` for a target list `tl` (use '' for an array-root target)
 * iterating source list `sl`. Direct source-child edges become relative paths;
 * function/intermediate feeders are folded in as item-relative expressions.
 * Consumes the edges it uses.
 */
function buildForeachMap(
  tl: string,
  iter: SourceIter,
  sourceNodeId: string,
  into: Edge[],
  nodes: Node[],
  edges: Edge[],
  consumed: Set<string>,
  functions: FunctionMap,
): Record<string, unknown> {
  const map: Record<string, unknown> = {};
  for (const e of into) {
    const th = e.targetHandle ?? '';
    const sh = e.sourceHandle ?? '';
    if ((tl !== '' && th === tl) || (tl === '' && th === ROOT_HANDLE)) {
      consumed.add(e.id); // the parent/iteration edge
      continue;
    }
    const isChild = tl === '' ? th !== '' && th !== ROOT_HANDLE : th.startsWith(`${tl}.`);
    if (!isChild) continue;
    const relTarget = tl === '' ? th : th.slice(tl.length + 1);

    const feeder = nodes.find((n) => n.id === e.source);
    if (!feeder) continue;

    if (feeder.id === sourceNodeId) {
      // A direct source field -> relative path within the item.
      const rel = relHandle(sh, iter.prefix);
      if (rel !== null) {
        map[relTarget] = rel;
        consumed.add(e.id);
      }
    } else {
      // A function/intermediate node -> fold its expression, made item-relative.
      const expr = buildExpr(feeder, e.sourceHandle, nodes, edges, functions);
      map[relTarget] = toInlineExpr(relativize(expr, iter.from));
      consumed.add(e.id);
    }
  }
  return map;
}

/** Resolve the source feeding a set of edges into a target list. */
function resolveSourceList(
  tl: string,
  into: Edge[],
  nodes: Node[],
): { sourceNode: Node; iter: SourceIter } | null {
  const isSource = (id: string | null) => nodes.find((n) => n.id === id)?.type === 'source';
  const parentEdge = tl === '' ? undefined : into.find((e) => e.targetHandle === tl);
  const directEdge = into.find((e) => isSource(e.source));

  const sourceNode =
    (parentEdge && isSource(parentEdge.source) && nodes.find((n) => n.id === parentEdge.source)) ||
    (directEdge && nodes.find((n) => n.id === directEdge.source)) ||
    null;
  if (!sourceNode) return null;

  // A root-array source iterates the input itself.
  if (sourceNode.data?.rootArray === true) {
    return { sourceNode, iter: { from: 'input[]', prefix: '' } };
  }

  const sourceLists = (sourceNode.data?.lists as string[] | undefined) ?? [];
  let sl = '';
  if (parentEdge && parentEdge.source === sourceNode.id) {
    sl = parentEdge.sourceHandle ?? '';
  } else {
    const sh = into.find((e) => e.source === sourceNode.id)?.sourceHandle ?? '';
    sl = sourceLists.find((l) => sh.startsWith(`${l}.`)) ?? '';
  }
  if (!sl || !sourceLists.includes(sl)) return null;
  return { sourceNode, iter: { from: `input.${sl}[]`, prefix: sl } };
}

/**
 * For an array-root target, find the source node and the source list every
 * connected field is projected from (tracing through function nodes). Picks the
 * most specific list that is an ancestor of all the fed source fields.
 */
function resolveSourceListForRoot(
  targetNodeId: string,
  nodes: Node[],
  edges: Edge[],
): { sourceNode: Node; iter: SourceIter } | null {
  const handles: string[] = [];
  let sourceNode: Node | undefined;
  const visit = (nodeId: string) => {
    for (const e of edges.filter((x) => x.target === nodeId)) {
      const feeder = nodes.find((n) => n.id === e.source);
      if (!feeder) continue;
      if (feeder.type === 'source') {
        sourceNode = feeder;
        handles.push(e.sourceHandle ?? '');
      } else {
        visit(feeder.id);
      }
    }
  };
  visit(targetNodeId);
  if (!sourceNode || handles.length === 0) return null;

  // A root-array source iterates the input itself; its fields are item-relative.
  if (sourceNode.data?.rootArray === true) {
    return { sourceNode, iter: { from: 'input[]', prefix: '' } };
  }

  const fieldHandles = handles.filter((h) => h !== ROOT_HANDLE);
  const lists = (sourceNode.data?.lists as string[] | undefined) ?? [];
  const sl = lists
    .filter((l) => fieldHandles.every((h) => h === l || h.startsWith(`${l}.`)))
    .sort((a, b) => b.length - a.length)[0];
  return sl ? { sourceNode, iter: { from: `input.${sl}[]`, prefix: sl } } : null;
}

/**
 * If a node's output ends up inside a target list (it's evaluated once per item),
 * return the source collection to iterate — so debug can show every result, not
 * just the first. Mirrors how compileGraphToDsl folds the node into a foreach.
 */
function nodeIterationFrom(node: Node, nodes: Node[], edges: Edge[]): string | null {
  const seen = new Set<string>();
  const stack = [node.id];
  while (stack.length) {
    const id = stack.pop()!;
    for (const e of edges.filter((x) => x.source === id)) {
      const down = nodes.find((n) => n.id === e.target);
      if (!down) continue;
      const th = e.targetHandle ?? '';
      if (down.type === 'target') {
        if (down.data?.rootArray === true) {
          const r = resolveSourceListForRoot(down.id, nodes, edges);
          if (r) return r.iter.from;
        }
        const lists = (down.data?.lists as string[] | undefined) ?? [];
        const tl = lists.find((l) => th === l || th.startsWith(`${l}.`));
        if (tl) {
          const into = edges.filter(
            (x) => x.target === down.id && (x.targetHandle === tl || (x.targetHandle ?? '').startsWith(`${tl}.`)),
          );
          const r = resolveSourceList(tl, into, nodes);
          if (r) return r.iter.from;
        }
      } else if (!seen.has(down.id)) {
        seen.add(down.id);
        stack.push(down.id);
      }
    }
  }
  return null;
}

/**
 * Build a standalone mapping that outputs a single node's value, for the debug
 * tracer. A node evaluated per list item is probed as a foreach so every result
 * shows; other intermediate nodes are wrapped as `{ value: <output> }`; the
 * Target uses the full graph DSL. Source nodes have no single output → null.
 */
export function compileNodeProbe(node: Node, nodes: Node[], edges: Edge[], functions: FunctionMap = {}): MappingDSL | null {
  if (node.type === 'source') return null;
  if (node.type === 'target') return compileGraphToDsl(nodes, edges, functions);

  const expr = buildExpr(node, 'out', nodes, edges, functions);
  if (expr == null) return null;

  const from = nodeIterationFrom(node, nodes, edges);
  if (from) {
    return { target: { type: 'array', from, map: { value: toInlineExpr(relativize(expr, from)) } } };
  }

  const rule = exprToRule('value', expr);
  const mappings = Array.isArray(rule) ? rule : rule ? [rule] : [];
  return mappings.length ? { target: { type: 'object', mappings } } : null;
}

/**
 * Compile a function document into a runnable mapping so it can be previewed and
 * tested on its own: the return value with each parameter bound to `input.<name>`
 * (the sample input is a JSON object of parameter values).
 */
export function compileFunctionDoc(nodes: Node[], edges: Edge[], functions: FunctionMap = {}): MappingDSL {
  const expr = buildReturnExpr(nodes, edges, functions, []);
  if (expr == null) return { target: { type: 'object', mappings: [] } };

  const params = nodes.filter((n) => n.type === 'param').map((n) => (n.data?.name as string) || 'param');
  const bound = substituteParams(expr, Object.fromEntries(params.map((p) => [p, `input.${p}`])));
  const rule = exprToRule('result', bound);
  const mappings = Array.isArray(rule) ? rule : rule ? [rule] : [];
  return { target: { type: 'object', mappings } };
}

/** Walk backward from every target field to build the nested mapping DSL. */
export function compileGraphToDsl(nodes: Node[], edges: Edge[], functions: FunctionMap = {}): MappingDSL {
  const targetNodes = nodes.filter((n) => n.type === 'target');
  const consumed = new Set<string>();

  // Array-root target: the whole output is a list projected from a source list.
  const rootTarget = targetNodes.find((n) => n.data?.rootArray === true);
  if (rootTarget) {
    const resolved = resolveSourceListForRoot(rootTarget.id, nodes, edges);
    if (resolved) {
      const into = edges.filter((e) => e.target === rootTarget.id);
      const map = buildForeachMap('', resolved.iter, resolved.sourceNode.id, into, nodes, edges, consumed, functions);
      if (Object.keys(map).length > 0) {
        return { target: { type: 'array', from: resolved.iter.from, map } };
      }
    }
  }

  const mapping: MappingDSL = { target: { type: 'object', mappings: [] } };
  const rules = mapping.target.mappings!;

  // Pass 1: list -> list. A target array fed from a source array becomes a
  // foreach iterating the source list, with child fields and functions folded
  // into the per-item map.
  for (const targetNode of targetNodes) {
    const targetLists = (targetNode.data?.lists as string[] | undefined) ?? [];
    for (const tl of targetLists) {
      const into = edges.filter(
        (e) => e.target === targetNode.id && (e.targetHandle === tl || (e.targetHandle ?? '').startsWith(`${tl}.`)),
      );
      if (into.length === 0) continue;

      const resolved = resolveSourceList(tl, into, nodes);
      if (!resolved) continue;
      const { sourceNode, iter } = resolved;

      const parentEdge = into.find((e) => e.targetHandle === tl);
      const map = buildForeachMap(tl, iter, sourceNode.id, into, nodes, edges, consumed, functions);

      if (Object.keys(map).length > 0) {
        rules.push({ target: tl, type: 'foreach', from: iter.from, map });
      } else if (parentEdge) {
        rules.push({ target: tl, type: 'fieldCopy', path: iter.from.replace(/\[\]$/, '') });
      }
    }
  }

  // Pass 2: everything not consumed by a list mapping.
  for (const targetNode of targetNodes) {
    for (const edge of edges.filter((e) => e.target === targetNode.id)) {
      if (consumed.has(edge.id)) continue;
      const fieldName = edge.targetHandle;
      const feeder = nodes.find((n) => n.id === edge.source);
      if (!fieldName || !feeder) continue;

      const expr = buildExpr(feeder, edge.sourceHandle, nodes, edges, functions);
      const rule = exprToRule(fieldName, expr);
      if (Array.isArray(rule)) rules.push(...rule);
      else if (rule) rules.push(rule);
    }
  }

  return mapping;
}
