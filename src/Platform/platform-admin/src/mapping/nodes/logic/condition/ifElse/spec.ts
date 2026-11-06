import { GitBranch } from 'lucide-react';
import type { NodeSpec } from '../../../base';
import { literalExpr, parseScalar } from '../../../compile-types';
import { IfElseNode } from './IfElseNode';

export const ifElseSpec: NodeSpec = {
  // DSL/runtime type stays 'condition'; the folder names the variant (ifElse).
  type: 'condition',
  group: 'logic',
  label: 'If / Else',
  icon: GitBranch,
  color: 'rose',
  component: IfElseNode,
  defaultData: () => ({
    label: 'Condition',
    combinator: 'all',
    clauses: [{ op: 'eq', value: '' }],
    then: 'Win',
    else: 'Lose',
  }),
  ports: { inputs: 'dynamic', output: true },
  help: {
    summary: 'Compares a wired value and outputs one of two results: the ternary of the canvas.',
    details: [
      'Wire a value into each numbered input, then pick an operator and a value to compare against.',
      'Several rows combine with ALL (AND) or ANY (OR).',
      'then / else take a literal, a path, or a wired node.',
      'has = text contains.',
    ],
    example: {
      caption: 'Pass mark',
      code: `{
  "target": "grade",
  "type": "condition",
  "if": { "op": "gte", "input": "input.score", "value": 80 },
  "then": "'Pass'",
  "else": "'Fail'"
}`,
    },
  },
  compile: (ctx) => {
    // A branch is a wired node's expression, or a typed literal fallback.
    const branch = (handle: string, literal: string) => {
      const input = ctx.inputExpr(handle);
      return input !== undefined ? input : literalExpr(literal);
    };

    // One leaf per clause; clause i reads its left operand from handle c{i}.
    const clauses = (ctx.node.data.clauses as { op: string; value: string }[] | undefined)
      ?? [{ op: 'eq', value: '' }];
    const leaves = clauses.map((clause, i) => {
      const leaf: Record<string, unknown> = { op: clause.op || 'eq', value: parseScalar(clause.value ?? '') };
      const edge = ctx.incomingEdge(`c${i}`);
      if (edge) {
        const upstream = ctx.nodes.find((n) => n.id === edge.source);
        if (upstream) leaf.input = ctx.build(upstream, edge.sourceHandle);
      }
      return leaf;
    });

    const combinator = ctx.node.data.combinator === 'any' ? 'any' : 'all';
    const ifExpr = leaves.length === 1 ? leaves[0] : { [combinator]: leaves };

    return {
      condition: {
        if: ifExpr,
        then: branch('then', (ctx.node.data.then as string) ?? ''),
        else: branch('else', (ctx.node.data.else as string) ?? ''),
      },
    };
  },
};
