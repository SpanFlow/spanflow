import type { Edge, Node } from '@xyflow/react';

/** A compiled DSL fragment: a literal, a path string, or a nested node object. */
export type Expr = string | number | boolean | Record<string, any> | null;

/**
 * Everything a node spec needs to compile its output expression, including
 * recursion into upstream nodes (which is what makes nodes composable).
 */
export interface CompileContext {
  node: Node;
  /** The handle on this node the downstream consumer connected to. */
  outHandle: string | null | undefined;
  nodes: Node[];
  edges: Edge[];
  /** Recursively compile any node's output. */
  build(node: Node, outHandle: string | null | undefined): Expr;
  /** The edge feeding `targetHandle` of this node (any input if omitted). */
  incomingEdge(targetHandle?: string): Edge | undefined;
  /** Compile whatever feeds `targetHandle`, or undefined if unwired. */
  inputExpr(targetHandle: string): Expr | undefined;
}

/** Parse user text into a JSON scalar: number, boolean, or string. */
export function parseScalar(text: string): string | number | boolean {
  const s = String(text ?? ''); // tolerate numbers/undefined from loaded data
  const t = s.trim();
  if (t !== '' && !Number.isNaN(Number(t))) return Number(t);
  if (t === 'true') return true;
  if (t === 'false') return false;
  return s;
}

/**
 * Turn user text into an engine *expression*. Numbers/booleans pass through;
 * strings are quoted so the engine treats them as literals (not paths).
 */
export function literalExpr(text: string): string | number | boolean {
  const scalar = parseScalar(text);
  return typeof scalar === 'string' ? `'${scalar}'` : scalar;
}

/**
 * Turn a source field handle like `orders.amount` into the engine's
 * collection + select form: `{ from: "input.orders[]", select: "amount" }`.
 * A single-segment field (no dot) is treated as the array itself.
 */
export function toFromSelect(fieldHandle: string): { from: string; select?: string } {
  const parts = fieldHandle.split('.').filter(Boolean);
  if (parts.length >= 2) {
    const select = parts.pop()!;
    const collection = parts.join('.');
    return { from: `input.${collection}[]`, select };
  }
  return { from: `input.${fieldHandle}[]` };
}
