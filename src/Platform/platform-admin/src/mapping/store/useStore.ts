import { create } from 'zustand';
import {
  addEdge,
  applyNodeChanges,
  applyEdgeChanges,
} from '@xyflow/react';
import type {
  Connection,
  Edge,
  EdgeChange,
  Node,
  NodeChange,
  OnNodesChange,
  OnEdgesChange,
  OnConnect,
} from '@xyflow/react';
import { v4 as uuidv4 } from 'uuid';
import { ufmappingApi } from '../../lib/ufmappingApi';
import { defaultNodeData } from '../nodes/registry';
import { inferStructure } from '../schema/inferFields';
import { matchChildConnections } from './autoConnect';
import { compileNodeProbe } from '../nodes/compile';
import { dslToGraph } from '../nodes/decompile';
import type { FunctionMap } from '../nodes/compile';
import { FUNCTION_EXAMPLES } from '../nodes/function/functionExamples';
import { TRACE_PATH } from '../config';
import type { NodeGroup } from '../../components/canvas/groups/nodeGroups';

export type AppNode = Node;

export interface NodeDebugResult {
  value: unknown;
  elapsedMs: number;
  error?: string;
}
export interface DebugState {
  active: boolean;
  running: boolean;
  results: Record<string, NodeDebugResult>;
  /** Node whose full request/response is shown in the debug modal, if any. */
  inspectNodeId: string | null;
}

/** A document (tab): a mapping or a reusable function definition. */
export type DocKind = 'mapping' | 'function';
export interface DocMeta {
  id: string;
  kind: DocKind;
  name: string;
  /** Set once the document has been saved to (or opened from) the server-side mapping store.
   * Saving again versions *this* mapping instead of creating a second one. */
  mappingId?: string;
  /** The version number the canvas currently corresponds to (bumped on every save). */
  mappingVersion?: number;
}
/** The editable graph state stored per (inactive) document. */
export interface DocGraph {
  nodes: AppNode[];
  edges: Edge[];
  sampleInput: string;
  /** Node groups on this canvas (E61-T5): a view only, which the compiler ignores. */
  groups?: NodeGroup[];
  past: Snapshot[];
  future: Snapshot[];
}

/** A new or reset canvas starts empty: the user adds the Input JSON (Source) node themselves. */
const blankCanvas = (): AppNode[] => [];

const emptyDebug = (): DebugState => ({ active: false, running: false, results: {}, inspectNodeId: null });

const DEFAULT_SAMPLE_INPUT = JSON.stringify(
  { orders: [{ id: 1, amount: 100 }, { id: 2, amount: 250 }, { id: 3, amount: 50 }] },
  null,
  2,
);

type Snapshot = { nodes: AppNode[]; edges: Edge[]; groups: NodeGroup[] };

const MAX_HISTORY = 50;
// Collapse pushes within this window into one (e.g. a node delete also removes
// its edges, firing two change callbacks in the same tick).
const COALESCE_MS = 80;
// A burst of edits to the same node (typing) is one undo step.
const EDIT_COALESCE_MS = 700;

interface AppState {
  nodes: AppNode[];
  edges: Edge[];
  /** The active document's node groups (E61-T5); saved with its graph, never compiled. */
  groups: NodeGroup[];
  past: Snapshot[];
  future: Snapshot[];
  sampleInput: string;
  /** Bumped whenever the graph is loaded/imported, so the canvas refits the view. */
  fitNonce: number;
  debug: DebugState;
  // Multi-document workspace. The active document's graph IS the top-level
  // nodes/edges/sampleInput/past/future; inactiveDocs holds the rest.
  docs: DocMeta[];
  activeDocId: string;
  inactiveDocs: Record<string, DocGraph>;
  theme: 'light' | 'dark';
  /** Node whose help modal is open, or null. */
  helpNodeId: string | null;
  // internal coalescing markers
  _lastPush: number;
  _lastEdit: { id: string; time: number } | null;
  toggleTheme: () => void;
  showHelp: (nodeId: string | null) => void;
  onNodesChange: OnNodesChange<AppNode>;
  onEdgesChange: OnEdgesChange;
  onConnect: OnConnect;
  addNode: (type: string, position: { x: number; y: number }, extraData?: Record<string, any>) => void;
  updateNodeData: (id: string, data: any) => void;
  setNodes: (nodes: AppNode[]) => void;
  setEdges: (edges: Edge[]) => void;
  /** Replaces the active document's groups; undoable. */
  setGroups: (groups: NodeGroup[]) => void;
  setSampleInput: (text: string) => void;
  loadGraph: (nodes: AppNode[], edges: Edge[], sampleInput?: string, groups?: NodeGroup[]) => void;
  importSource: (text: string) => void;
  importTarget: (text: string) => void;
  pushHistory: () => void;
  undo: () => void;
  redo: () => void;
  runDebug: () => Promise<void>;
  setDebugActive: (active: boolean) => void;
  clearDebug: () => void;
  inspectNode: (id: string | null) => void;
  // Documents / tabs
  newTab: (kind: DocKind, name: string, graph?: Partial<DocGraph>) => string;
  switchTab: (id: string) => void;
  closeTab: (id: string) => void;
  renameTab: (id: string, name: string) => void;
  openInNewTab: (name: string, nodes: AppNode[], edges: Edge[], sampleInput?: string, groups?: NodeGroup[]) => string;
  /** Bind a document to a saved mapping (after opening one, or after the first save of a new one). */
  bindSavedMapping: (docId: string, mappingId: string, version: number, name?: string) => void;
  /** Forget the binding — used when the mapping is deleted from the store under an open tab. */
  unbindSavedMapping: (mappingId: string) => void;
  /** Replace the active canvas with a mapping definition (the "paste transform JSON" path). */
  loadDefinition: (definition: unknown, sampleInput?: string) => void;
  hydrateWorkspace: (data: WorkspaceSnapshot) => void;
  serializeWorkspace: () => WorkspaceSnapshot;
  /**
   * Back to a single empty Main tab (E16-T5). Called when the designer switches to another
   * project, *before* that project's own workspace is hydrated — without it, a project with no
   * saved workspace would simply keep showing the previous project's tabs.
   */
  resetWorkspace: () => void;
  newFunction: (name: string) => string;
  buildFunctions: () => FunctionMap;
  /** Return the id of a function doc with this name, creating it from the examples if needed. */
  ensureFunction: (name: string) => string;
}

/** The persisted shape of the whole workspace (for autosave/restore). */
export interface WorkspaceSnapshot {
  docs: DocMeta[];
  activeDocId: string;
  graphs: Record<string, { nodes: AppNode[]; edges: Edge[]; sampleInput: string; groups?: NodeGroup[] }>;
}

export const useStore = create<AppState>((set, get) => ({
  nodes: blankCanvas(),
  edges: [],
  groups: [],
  past: [],
  future: [],
  sampleInput: DEFAULT_SAMPLE_INPUT,
  fitNonce: 0,
  debug: emptyDebug(),
  docs: [{ id: 'main', kind: 'mapping', name: 'Main' }],
  activeDocId: 'main',
  inactiveDocs: {},
  _lastPush: 0,
  _lastEdit: null,
  // platform-admin renders light; the canvas colorMode follows this field.
  theme: 'light',
  helpNodeId: null,
  showHelp: (nodeId) => set({ helpNodeId: nodeId }),
  toggleTheme: () => {
    const newTheme = get().theme === 'light' ? 'dark' : 'light';
    if (newTheme === 'dark') {
      document.documentElement.classList.add('dark');
    } else {
      document.documentElement.classList.remove('dark');
    }
    set({ theme: newTheme });
  },

  // Snapshot the current graph so it can be undone. Coalesces rapid calls.
  pushHistory: () => {
    const now = Date.now();
    const { nodes, edges, groups, past, _lastPush } = get();
    if (now - _lastPush < COALESCE_MS) return;
    const next = [...past, { nodes, edges, groups }];
    if (next.length > MAX_HISTORY) next.shift();
    set({ past: next, future: [], _lastPush: now });
  },

  undo: () => {
    const { past, future, nodes, edges, groups } = get();
    if (past.length === 0) return;
    const previous = past[past.length - 1];
    set({
      nodes: previous.nodes,
      edges: previous.edges,
      groups: previous.groups ?? [],
      past: past.slice(0, -1),
      future: [{ nodes, edges, groups }, ...future],
      _lastPush: 0,
      _lastEdit: null,
    });
  },

  redo: () => {
    const { past, future, nodes, edges, groups } = get();
    if (future.length === 0) return;
    const next = future[0];
    set({
      nodes: next.nodes,
      edges: next.edges,
      groups: next.groups ?? [],
      past: [...past, { nodes, edges, groups }],
      future: future.slice(1),
      _lastPush: 0,
      _lastEdit: null,
    });
  },

  onNodesChange: (changes: NodeChange<AppNode>[]) => {
    const removed = changes.filter((c) => c.type === 'remove').map((c) => c.id);
    // Removals are undoable; position/selection churn is not.
    if (removed.length) get().pushHistory();

    const nodes = applyNodeChanges(changes, get().nodes);
    if (removed.length) {
      // Drop any edges left dangling by a deleted node so the DSL recompiles.
      const gone = new Set(removed);
      const edges = get().edges.filter((e) => !gone.has(e.source) && !gone.has(e.target));
      set({ nodes, edges });
    } else {
      set({ nodes });
    }
  },
  onEdgesChange: (changes: EdgeChange[]) => {
    if (changes.some((c) => c.type === 'remove')) get().pushHistory();
    set({ edges: applyEdgeChanges(changes, get().edges) });
  },
  onConnect: (connection: Connection) => {
    get().pushHistory();
    let edges = addEdge(connection, get().edges);
    // Parent-to-parent (Source->Target) auto-maps same-named children.
    for (const child of matchChildConnections(connection, get().nodes, edges)) {
      edges = addEdge(child, edges);
    }
    set({ edges });
  },
  addNode: (type: string, position: { x: number; y: number }, extraData?: Record<string, any>) => {
    get().pushHistory();
    const newNode: AppNode = {
      id: uuidv4(),
      type,
      position,
      data: { ...defaultNodeData(type), ...extraData },
    };
    set({ nodes: [...get().nodes, newNode] });
  },
  updateNodeData: (id: string, data: any) => {
    const now = Date.now();
    const last = get()._lastEdit;
    if (!(last && last.id === id && now - last.time < EDIT_COALESCE_MS)) {
      get().pushHistory();
    }
    set({
      _lastEdit: { id, time: now },
      nodes: get().nodes.map((n) =>
        n.id === id ? { ...n, data: { ...n.data, ...data } } : n
      ),
    });
  },
  setNodes: (nodes) => set({ nodes }),
  setEdges: (edges) => set({ edges }),
  setGroups: (groups) => {
    get().pushHistory();
    set({ groups });
  },
  setSampleInput: (text) => set({ sampleInput: text }),

  // Replace the whole graph (e.g. loading an Examples recipe), undoable.
  loadGraph: (nodes, edges, sampleInput, groups) => {
    get().pushHistory();
    set({ nodes, edges, groups: groups ?? [], fitNonce: get().fitNonce + 1, debug: emptyDebug(), ...(sampleInput !== undefined ? { sampleInput } : {}) });
  },

  // ---- Documents / tabs ---------------------------------------------------

  newTab: (kind, name, graph) => {
    const s = get();
    const id = `doc-${Date.now()}-${Math.random().toString(36).slice(2, 6)}`;
    const active: DocGraph = { nodes: s.nodes, edges: s.edges, groups: s.groups, sampleInput: s.sampleInput, past: s.past, future: s.future };
    set({
      docs: [...s.docs, { id, kind, name }],
      inactiveDocs: { ...s.inactiveDocs, [s.activeDocId]: active },
      activeDocId: id,
      nodes: graph?.nodes ?? blankCanvas(),
      edges: graph?.edges ?? [],
      groups: graph?.groups ?? [],
      sampleInput: graph?.sampleInput ?? DEFAULT_SAMPLE_INPUT,
      past: [],
      future: [],
      debug: emptyDebug(),
      fitNonce: s.fitNonce + 1,
    });
    return id;
  },

  switchTab: (id) => {
    const s = get();
    if (id === s.activeDocId || !s.docs.some((d) => d.id === id)) return;
    const active: DocGraph = { nodes: s.nodes, edges: s.edges, groups: s.groups, sampleInput: s.sampleInput, past: s.past, future: s.future };
    const target = s.inactiveDocs[id] ?? { nodes: blankCanvas(), edges: [], sampleInput: DEFAULT_SAMPLE_INPUT, past: [], future: [] };
    const { [id]: _removed, ...rest } = s.inactiveDocs;
    set({
      inactiveDocs: { ...rest, [s.activeDocId]: active },
      activeDocId: id,
      nodes: target.nodes,
      edges: target.edges,
      groups: target.groups ?? [],
      sampleInput: target.sampleInput,
      past: target.past,
      future: target.future,
      debug: emptyDebug(),
      fitNonce: s.fitNonce + 1,
    });
  },

  closeTab: (id) => {
    const s = get();
    if (s.docs.length <= 1) return; // always keep one tab
    const remaining = s.docs.filter((d) => d.id !== id);
    const { [id]: _closed, ...rest } = s.inactiveDocs;
    if (id !== s.activeDocId) {
      set({ docs: remaining, inactiveDocs: rest });
      return;
    }
    // Closing the active tab: fall back to the last remaining one.
    const nextId = remaining[remaining.length - 1].id;
    const target = rest[nextId] ?? { nodes: blankCanvas(), edges: [], sampleInput: DEFAULT_SAMPLE_INPUT, past: [], future: [] };
    const { [nextId]: _next, ...rest2 } = rest;
    set({
      docs: remaining,
      inactiveDocs: rest2,
      activeDocId: nextId,
      nodes: target.nodes,
      edges: target.edges,
      groups: target.groups ?? [],
      sampleInput: target.sampleInput,
      past: target.past,
      future: target.future,
      debug: emptyDebug(),
      fitNonce: s.fitNonce + 1,
    });
  },

  renameTab: (id, name) => set({ docs: get().docs.map((d) => (d.id === id ? { ...d, name } : d)) }),

  openInNewTab: (name, nodes, edges, sampleInput, groups) => get().newTab('mapping', name, { nodes, edges, sampleInput, groups }),

  bindSavedMapping: (docId, mappingId, version, name) =>
    set({
      docs: get().docs.map((d) =>
        d.id === docId ? { ...d, mappingId, mappingVersion: version, name: name ?? d.name } : d,
      ),
    }),

  unbindSavedMapping: (mappingId) =>
    set({
      docs: get().docs.map((d) =>
        d.mappingId === mappingId ? { ...d, mappingId: undefined, mappingVersion: undefined } : d,
      ),
    }),

  // Reverse-compile a mapping definition into a canvas. Used by "paste transform JSON" and by
  // opening a saved mapping that has no stored graph (one that was itself saved from pasted JSON).
  loadDefinition: (definition, sampleInput) => {
    const graph = dslToGraph(definition as never);
    get().loadGraph(graph.nodes as AppNode[], graph.edges as Edge[], sampleInput);
  },

  newFunction: (name) => {
    const ret = { id: `ret-${Date.now()}`, type: 'return', position: { x: 620, y: 140 }, data: { label: 'Return', fields: ['value'] } } as AppNode;
    return get().newTab('function', name, { nodes: [ret], edges: [], sampleInput: '{}' });
  },

  ensureFunction: (name) => {
    const existing = get().docs.find((d) => d.kind === 'function' && d.name === name);
    if (existing) return existing.id;
    const ex = FUNCTION_EXAMPLES.find((e) => e.name === name);
    if (!ex) return '';
    return get().newTab('function', name, {
      nodes: JSON.parse(JSON.stringify(ex.nodes)),
      edges: JSON.parse(JSON.stringify(ex.edges)),
      sampleInput: ex.sampleInput,
    });
  },

  // A function definition per function document (params from its Parameter nodes).
  buildFunctions: () => {
    const s = get();
    const map: FunctionMap = {};
    for (const d of s.docs) {
      if (d.kind !== 'function') continue;
      const g = d.id === s.activeDocId ? { nodes: s.nodes, edges: s.edges } : s.inactiveDocs[d.id];
      if (!g) continue;
      const params = g.nodes.filter((n) => n.type === 'param').map((n) => (n.data?.name as string) || 'param');
      map[d.id] = { id: d.id, name: d.name, params, nodes: g.nodes, edges: g.edges };
    }
    return map;
  },

  serializeWorkspace: () => {
    const s = get();
    const graphs: WorkspaceSnapshot['graphs'] = {};
    for (const [id, g] of Object.entries(s.inactiveDocs)) {
      graphs[id] = { nodes: g.nodes, edges: g.edges, sampleInput: g.sampleInput, groups: g.groups ?? [] };
    }
    graphs[s.activeDocId] = { nodes: s.nodes, edges: s.edges, sampleInput: s.sampleInput, groups: s.groups };
    return { docs: s.docs, activeDocId: s.activeDocId, graphs };
  },

  resetWorkspace: () =>
    set({
      docs: [{ id: 'main', kind: 'mapping', name: 'Main' }],
      activeDocId: 'main',
      inactiveDocs: {},
      nodes: blankCanvas(),
      edges: [],
      groups: [],
      sampleInput: DEFAULT_SAMPLE_INPUT,
      past: [],
      future: [],
      debug: emptyDebug(),
      fitNonce: get().fitNonce + 1,
    }),

  hydrateWorkspace: (data) => {
    if (!data?.docs?.length) return;
    const activeId = data.graphs[data.activeDocId] ? data.activeDocId : data.docs[0].id;
    const inactiveDocs: Record<string, DocGraph> = {};
    for (const d of data.docs) {
      if (d.id === activeId) continue;
      const g = data.graphs[d.id];
      if (g) inactiveDocs[d.id] = { ...g, past: [], future: [] };
    }
    const a = data.graphs[activeId] ?? { nodes: blankCanvas(), edges: [], sampleInput: DEFAULT_SAMPLE_INPUT };
    set({
      docs: data.docs,
      activeDocId: activeId,
      inactiveDocs,
      nodes: a.nodes,
      edges: a.edges,
      groups: a.groups ?? [],
      sampleInput: a.sampleInput,
      past: [],
      future: [],
      debug: emptyDebug(),
      fitNonce: get().fitNonce + 1,
    });
  },

  setDebugActive: (active) => set({ debug: { ...get().debug, active } }),
  clearDebug: () => set({ debug: { active: false, running: false, results: {}, inspectNodeId: null } }),
  inspectNode: (id) => set({ debug: { ...get().debug, inspectNodeId: id } }),

  // Evaluate every node's output (one probe each) and record value + timing so
  // each node can show what it received/produced when run on the sample input.
  runDebug: async () => {
    const { nodes, edges, sampleInput } = get();
    let inputObject: unknown;
    try {
      inputObject = JSON.parse(sampleInput);
    } catch {
      set({ debug: { active: true, running: false, results: {}, inspectNodeId: null } });
      return;
    }

    // kind: 'target' (whole result), 'array' (per-item foreach probe → [{value}]), 'object' ({value}).
    const functions = get().buildFunctions();
    const probes = nodes
      .map((n) => {
        const mapping = compileNodeProbe(n, nodes, edges, functions) as any;
        const kind = n.type === 'target' ? 'target' : mapping?.target?.type === 'array' ? 'array' : 'object';
        return { id: n.id, kind, mapping };
      })
      .filter((p) => p.mapping !== null);

    set({ debug: { active: true, running: true, results: {}, inspectNodeId: null } });
    try {
      const res = await ufmappingApi.post(TRACE_PATH, {
        inputObject,
        probes: probes.map((p) => ({ id: p.id, mapping: p.mapping })),
      });
      const kindOf = new Map(probes.map((p) => [p.id, p.kind]));
      const results: Record<string, NodeDebugResult> = {};
      for (const r of res.data as { id: string; output: any; elapsedMs: number; error?: string }[]) {
        let value: unknown;
        if (r.error) value = undefined;
        else if (kindOf.get(r.id) === 'target') value = r.output;
        else if (kindOf.get(r.id) === 'array') value = Array.isArray(r.output) ? r.output.map((x: any) => x?.value) : r.output;
        else value = r.output?.value;
        results[r.id] = { value, elapsedMs: r.elapsedMs, error: r.error };
      }
      set({ debug: { active: true, running: false, results, inspectNodeId: null } });
    } catch {
      set({ debug: { active: true, running: false, results: {}, inspectNodeId: null } });
    }
  },

  // Build the Source structure from a sample document and use it as the input.
  importSource: (text) => {
    const json = JSON.parse(text);
    const { fields, lists, types, rootArray } = inferStructure(json);
    get().pushHistory();
    const nodes = get().nodes;
    const src = nodes.find((n) => n.type === 'source');
    const data = { label: 'Source', fields: fields.length ? fields : ['field'], lists, types, rootArray };
    const nextNodes = src
      ? nodes.map((n) => (n.id === src.id ? { ...n, data: { ...n.data, ...data } } : n))
      : [{ id: uuidv4(), type: 'source', position: { x: 60, y: 80 }, data } as AppNode, ...nodes];
    set({ nodes: nextNodes, sampleInput: JSON.stringify(json, null, 2), fitNonce: get().fitNonce + 1 });
  },

  // Build the Target structure from a sample of the desired output.
  importTarget: (text) => {
    const json = JSON.parse(text);
    const { fields, lists, types, rootArray } = inferStructure(json);
    get().pushHistory();
    const nodes = get().nodes;
    const tgt = nodes.find((n) => n.type === 'target');
    const data = { label: 'Target', fields: fields.length ? fields : ['result'], lists, types, rootArray };
    const nextNodes = tgt
      ? nodes.map((n) => (n.id === tgt.id ? { ...n, data: { ...n.data, ...data } } : n))
      : [...nodes, { id: uuidv4(), type: 'target', position: { x: 940, y: 80 }, data } as AppNode];
    set({ nodes: nextNodes, fitNonce: get().fitNonce + 1 });
  },
}));
