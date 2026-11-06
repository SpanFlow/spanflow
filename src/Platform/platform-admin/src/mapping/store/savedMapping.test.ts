import { describe, it, expect, beforeEach } from 'vitest';
import { useStore } from './useStore';

// The designer half of the saved-mapping feature: a tab remembers which stored mapping it came
// from (so the next save versions that one), and a pasted definition becomes a real canvas.

const reset = () =>
  useStore.setState({
    docs: [{ id: 'main', kind: 'mapping', name: 'Main' }],
    activeDocId: 'main',
    inactiveDocs: {},
    nodes: [],
    edges: [],
    past: [],
    future: [],
  });

describe('saved-mapping binding', () => {
  beforeEach(reset);

  it('binds a document to a stored mapping and its version', () => {
    useStore.getState().bindSavedMapping('main', 'map-1', 3, 'Orders');

    const doc = useStore.getState().docs.find((d) => d.id === 'main')!;
    expect(doc.mappingId).toBe('map-1');
    expect(doc.mappingVersion).toBe(3);
    expect(doc.name).toBe('Orders'); // the tab takes the mapping's name
  });

  it('unbinds every tab of a mapping that was deleted from the store', () => {
    const other = useStore.getState().openInNewTab('Second', [], []);
    useStore.getState().bindSavedMapping('main', 'map-1', 1);
    useStore.getState().bindSavedMapping(other, 'map-2', 1);

    useStore.getState().unbindSavedMapping('map-1');

    const docs = useStore.getState().docs;
    expect(docs.find((d) => d.id === 'main')!.mappingId).toBeUndefined();
    expect(docs.find((d) => d.id === other)!.mappingId).toBe('map-2'); // untouched
  });
});

describe('loadDefinition (paste transform JSON)', () => {
  beforeEach(reset);

  it('reverse-compiles a pasted definition into a canvas', () => {
    useStore.getState().loadDefinition({
      target: { type: 'object', mappings: [{ target: 'orderId', from: 'input.id' }] },
    });

    const { nodes, edges } = useStore.getState();
    expect(nodes.some((n) => n.type === 'source')).toBe(true);
    expect(nodes.some((n) => n.type === 'target')).toBe(true);
    expect(edges.length).toBeGreaterThan(0);
  });

  it('replaces the sample input when one is supplied', () => {
    useStore.getState().loadDefinition(
      { target: { type: 'object', mappings: [{ target: 'a', from: 'input.a' }] } },
      '{ "a": 1 }',
    );

    expect(useStore.getState().sampleInput).toBe('{ "a": 1 }');
  });
});
