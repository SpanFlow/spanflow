import { describe, it, expect, beforeEach } from 'vitest';
import type { Edge, Node } from '@xyflow/react';
import { useStore } from './useStore';

const node = (id: string, type = 'source'): Node => ({ id, type, position: { x: 0, y: 0 }, data: {} } as Node);

describe('node deletion', () => {
  beforeEach(() => {
    useStore.setState({
      nodes: [node('a'), node('mid', 'aggregate'), node('b', 'target')],
      edges: [
        { id: 'e1', source: 'a', target: 'mid' } as Edge,
        { id: 'e2', source: 'mid', target: 'b' } as Edge,
      ],
      past: [],
      future: [],
    });
  });

  it('removes edges connected to a deleted node', () => {
    useStore.getState().onNodesChange([{ type: 'remove', id: 'mid' } as any]);
    const s = useStore.getState();
    expect(s.nodes.map((n) => n.id)).toEqual(['a', 'b']);
    expect(s.edges).toEqual([]); // both edges touched 'mid'
  });

  it('leaves unrelated edges intact', () => {
    useStore.getState().onNodesChange([{ type: 'remove', id: 'a' } as any]);
    const s = useStore.getState();
    expect(s.edges.map((e) => e.id)).toEqual(['e2']); // only e1 touched 'a'
  });
});

describe('a new mapping starts empty', () => {
  it('adds no Input JSON node to a new tab or a reset workspace: the user adds their own', () => {
    useStore.getState().resetWorkspace();
    expect(useStore.getState().nodes).toEqual([]);

    useStore.getState().newTab('mapping', 'Second');
    expect(useStore.getState().nodes).toEqual([]);
    expect(useStore.getState().edges).toEqual([]);
  });
});
