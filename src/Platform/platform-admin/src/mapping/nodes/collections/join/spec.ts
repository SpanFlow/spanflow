import { GitMerge } from 'lucide-react';
import type { NodeSpec } from '../../base';
import { JoinNode } from './JoinNode';

export const joinSpec: NodeSpec = {
  type: 'join',
  group: 'collections',
  label: 'Join',
  icon: GitMerge,
  color: 'pink',
  component: JoinNode,
  defaultData: () => ({ label: 'Join', leftKey: '', rightKey: '' }),
  ports: { inputs: 'dynamic', output: true },
  help: {
    summary: 'Merges two lists into one: rows whose keys match are combined into a single object.',
    details: [
      'Wire a collection into each input, then name the key field on each side.',
      'The keys may have different names (e.g. customerId on the left, custId on the right).',
      'A left row with no match on the right is dropped.',
    ],
    example: {
      caption: 'Orders joined to their customer',
      code: `{
  "target": "rows",
  "type": "join",
  "left": "input.orders[]",
  "right": "input.customers[]",
  "leftKey": "customerId",
  "rightKey": "custId"
}`,
    },
  },
  compile: (ctx) => {
    const join: Record<string, unknown> = {
      leftKey: (ctx.node.data.leftKey as string) || '',
      rightKey: (ctx.node.data.rightKey as string) || '',
    };

    // Each side reads a source collection as a path, or a nested expression.
    const side = (handle: string, pathKey: string, inputKey: string) => {
      const edge = ctx.incomingEdge(handle);
      if (!edge) return;
      const upstream = ctx.nodes.find((n) => n.id === edge.source);
      if (!upstream) return;
      if (upstream.type === 'source' && edge.sourceHandle) {
        join[pathKey] = `input.${edge.sourceHandle}[]`;
      } else {
        join[inputKey] = ctx.build(upstream, edge.sourceHandle);
      }
    };
    side('left', 'left', 'leftInput');
    side('right', 'right', 'rightInput');

    return { join };
  },
};
