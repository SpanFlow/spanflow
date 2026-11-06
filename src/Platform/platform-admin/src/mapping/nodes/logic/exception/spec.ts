import { AlertTriangle } from 'lucide-react';
import type { NodeSpec } from '../../base';
import { parseScalar } from '../../compile-types';
import { ExceptionNode } from './ExceptionNode';

export const exceptionSpec: NodeSpec = {
  type: 'exception',
  group: 'logic',
  label: 'Exception',
  icon: AlertTriangle,
  color: 'red',
  component: ExceptionNode,
  defaultData: () => ({ label: 'Exception', op: 'lt', value: '0', message: 'Invalid value' }),
  ports: { inputs: 'one', output: true },
  help: {
    summary:
      'Raises a mapping error when the wired value matches the condition; otherwise passes the value straight through.',
    details: [
      'Use it to fail loudly on bad data rather than writing a wrong value downstream.',
      'The whole mapping is rejected: the message is what the caller sees.',
      'When the condition does not match, the value passes through untouched, so the node can sit mid-chain.',
    ],
    example: {
      caption: 'Reject a negative total',
      code: `{
  "target": "total",
  "type": "exception",
  "from": "input.total",
  "op": "lt",
  "value": 0,
  "message": "Total cannot be negative"
}`,
    },
  },
  compile: (ctx) => {
    const exception: Record<string, unknown> = {
      op: (ctx.node.data.op as string) || 'eq',
      value: parseScalar((ctx.node.data.value as string) ?? ''),
      message: (ctx.node.data.message as string) || 'Validation failed',
    };

    const edge = ctx.incomingEdge('in');
    if (edge) {
      const upstream = ctx.nodes.find((n) => n.id === edge.source);
      if (upstream) {
        const expr = ctx.build(upstream, edge.sourceHandle);
        if (typeof expr === 'string') exception.from = expr;
        else exception.input = expr;
      }
    }
    return { exception };
  },
};
