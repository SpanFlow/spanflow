import { describe, it, expect, beforeEach } from 'vitest';
import type { Edge, Node } from '@xyflow/react';
import { groupsOf, storedGraph } from './graphGroups';
import { useStore } from './useStore';

// E61-T5: a mapping canvas's node groups travel with its graph everywhere the graph goes (tabs, undo,
// the workspace autosave, the stored mapping) and are dropped only where the canvas is rebuilt from a
// definition, which makes fresh nodes no group could name.

const node = (id: string): Node => ({ id, type: 'source', position: { x: 0, y: 0 }, data: {} } as Node);
const lookup = (collapsed = true) => [{ id: 'g1', name: 'Lookup', nodeIds: ['a', 'b'], collapsed }];

describe('storedGraph', () => {
  it('leaves the groups key out when there are none, as a graph saved before E61 looks', () => {
    expect(storedGraph([node('a')], [], [])).toEqual({ nodes: [node('a')], edges: [] });
    expect('groups' in storedGraph([node('a')], [], undefined)).toBe(false);
  });

  it('cleans groups against the nodes being stored', () => {
    expect(storedGraph([node('a'), node('b')], [], lookup()).groups).toEqual(lookup());
    // b was deleted: a group of one is no group.
    expect('groups' in storedGraph([node('a')], [], lookup())).toBe(false);
  });

  it('reads groups off anything, and nothing off something unreadable', () => {
    expect(groupsOf({ nodes: [], edges: [], groups: lookup() })).toEqual(lookup());
    expect(groupsOf({ nodes: [], edges: [] })).toEqual([]);
    expect(groupsOf(null)).toEqual([]);
    expect(groupsOf({ groups: 'nope' })).toEqual([]);
  });
});

describe('groups in the designer store', () => {
  beforeEach(() =>
    useStore.setState({
      docs: [{ id: 'main', kind: 'mapping', name: 'Main' }],
      activeDocId: 'main',
      inactiveDocs: {},
      nodes: [node('a'), node('b')],
      edges: [] as Edge[],
      groups: lookup(),
      past: [],
      future: [],
      _lastPush: 0,
    }),
  );

  it('keeps each tab its own groups when switching away and back', () => {
    const other = useStore.getState().openInNewTab('Other', [node('x')], []);
    expect(useStore.getState().groups).toEqual([]);
    useStore.getState().switchTab('main');
    expect(useStore.getState().groups).toEqual(lookup());
    useStore.getState().switchTab(other);
    expect(useStore.getState().groups).toEqual([]);
  });

  it('opens a tab with the groups it was given, as a saved mapping or scenario does', () => {
    useStore.getState().openInNewTab('Saved', [node('a'), node('b')], [], undefined, lookup(false));
    expect(useStore.getState().groups).toEqual(lookup(false));
  });

  it('undoes and redoes a grouping change', () => {
    useStore.getState().setGroups([]);
    expect(useStore.getState().groups).toEqual([]);
    useStore.getState().undo();
    expect(useStore.getState().groups).toEqual(lookup());
    useStore.getState().redo();
    expect(useStore.getState().groups).toEqual([]);
  });

  it('round-trips groups through the workspace autosave', () => {
    const snapshot = useStore.getState().serializeWorkspace();
    useStore.getState().resetWorkspace();
    expect(useStore.getState().groups).toEqual([]);
    useStore.getState().hydrateWorkspace(snapshot);
    expect(useStore.getState().groups).toEqual(lookup());
  });

  it('starts with no groups when the canvas is rebuilt from a definition', () => {
    useStore.getState().loadDefinition({ target: { type: 'object', mappings: [{ target: 'id', from: 'input.id' }] } });
    expect(useStore.getState().groups).toEqual([]);
  });
});
