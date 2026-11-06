import { ListFilter } from 'lucide-react';
import type { NodeSpec } from '../../base';
import { parseScalar } from '../../compile-types';
import { WhereNode } from './WhereNode';

type Clause = { field: string; op: string; value: string };

/** Compile the clause rows into an engine WhereDefinition. */
export const buildWhere = (data: Record<string, any>): Record<string, unknown> | undefined => {
  const clauses = ((data.clauses as Clause[] | undefined) ?? []).filter((c) => c.field);
  if (clauses.length === 0) return undefined;

  const leaf = (c: Clause) => ({ field: c.field, op: c.op || 'eq', value: parseScalar(c.value ?? '') });

  // A lone rule needs no all/any wrapper — the engine takes a bare leaf clause.
  if (clauses.length === 1) return leaf(clauses[0]);

  const match = data.match === 'any' ? 'any' : 'all';
  return { [match]: clauses.map(leaf) };
};

export const whereSpec: NodeSpec = {
  type: 'where',
  group: 'logic',
  label: 'Where',
  icon: ListFilter,
  color: 'amber',
  component: WhereNode,
  defaultData: () => ({ label: 'Where', match: 'all', clauses: [{ field: '', op: 'eq', value: '' }] }),
  // A standalone predicate: nothing feeds it, it feeds a node's where/having port.
  ports: { inputs: 'none', output: true },
  // A condition is not a value, so it can only be dropped on a port that asks
  // for one (Group By's where/having) — never into a Target field or an input.
  outputOnlyToAccepting: true,
  help: {
    summary:
      "A reusable condition. It produces no value of its own: wire it into a port that asks for a condition, such as Group By's where or having.",
    details: [
      'match: all means every rule must pass (AND). match: any means at least one must pass (OR).',
      "Into Group By's where port: rules name a field of an input ROW (e.g. status, amount), and non-matching rows are dropped before grouping.",
      "Into Group By's having port: rules name an OUTPUT field of the group (e.g. total, orderCount), and non-matching groups are dropped after the aggregates run.",
      'Values are read as JSON: 100 is a number, true is a boolean, anything else is text.',
    ],
    example: {
      caption: 'Completed OR at least 100: compiles to',
      code: `{
  "any": [
    { "field": "status", "op": "eq",  "value": "Completed" },
    { "field": "amount", "op": "gte", "value": 100 }
  ]
}`,
    },
  },
  compile: (ctx) => buildWhere(ctx.node.data) ?? null,
};
