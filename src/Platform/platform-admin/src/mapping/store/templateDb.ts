import type { Edge, Node } from '@xyflow/react';
import type { NodeGroup } from '../../components/canvas/groups/nodeGroups';
import { storedGraph } from './graphGroups';

// A tiny IndexedDB layer for saved mapping "scenarios" (templates). Persists the
// graph + sample input in the browser so work survives reloads and can be
// reloaded or deleted from the History panel.
//
// Everything here is scoped to the Platform **project** the designer is open in (E16-T5).
// It used to be global — one `templates` store and one workspace record keyed `'current'` —
// so opening the designer in another project restored the previous project's canvas and then
// autosaved over it. A project id is therefore part of every read and write below.

export interface Template {
  id: string;
  name: string;
  createdAt: number;
  nodes: Node[];
  edges: Edge[];
  sampleInput: string;
  /** Node groups on the canvas (E61-T5); absent on scenarios saved before E61 or with none. */
  groups?: NodeGroup[];
  /** The project this scenario belongs to. Absent only on rows written before E16-T5. */
  projectId?: string;
}

const DB_NAME = 'maplite';
const STORE = 'templates';
const WORKSPACE = 'workspace';
const VERSION = 3;

/** No project in context (the designer rendered outside a project route) gets its own bucket. */
const NO_PROJECT = '__none__';

const scope = (projectId?: string | null) => projectId || NO_PROJECT;

function openDb(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const req = indexedDB.open(DB_NAME, VERSION);
    req.onupgradeneeded = () => {
      const db = req.result;
      if (!db.objectStoreNames.contains(STORE)) db.createObjectStore(STORE, { keyPath: 'id' });
      if (!db.objectStoreNames.contains(WORKSPACE)) db.createObjectStore(WORKSPACE); // out-of-line keys
      // v2 → v3 adds no index: scenarios are few and filtered in memory, and an index would need a
      // backfill over rows that have no projectId yet — see the adoption in listTemplates.
    };
    req.onsuccess = () => resolve(req.result);
    req.onerror = () => reject(req.error);
  });
}

async function withStore<T>(mode: IDBTransactionMode, fn: (store: IDBObjectStore) => IDBRequest): Promise<T> {
  const db = await openDb();
  return new Promise<T>((resolve, reject) => {
    const tx = db.transaction(STORE, mode);
    const req = fn(tx.objectStore(STORE));
    req.onsuccess = () => resolve(req.result as T);
    req.onerror = () => reject(req.error);
    tx.oncomplete = () => db.close();
  });
}

/** Keep only the serializable graph shape (drop transient UI state). */
const clean = (nodes: Node[], edges: Edge[]) => ({
  nodes: nodes.map((n) => ({ id: n.id, type: n.type, position: n.position, data: n.data })) as Node[],
  edges: edges.map((e) => ({
    id: e.id,
    source: e.source,
    target: e.target,
    sourceHandle: e.sourceHandle,
    targetHandle: e.targetHandle,
  })) as Edge[],
});

export async function saveTemplate(
  name: string,
  nodes: Node[],
  edges: Edge[],
  sampleInput: string,
  projectId?: string | null,
  groups?: readonly NodeGroup[],
): Promise<Template> {
  const g = clean(nodes, edges);
  const stored = storedGraph(g.nodes, g.edges, groups);
  const template: Template = {
    id: `tpl-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`,
    name,
    createdAt: Date.now(),
    nodes: g.nodes,
    edges: g.edges,
    sampleInput,
    ...(stored.groups ? { groups: stored.groups } : {}),
    projectId: scope(projectId),
  };
  await withStore('readwrite', (s) => s.put(template));
  return template;
}

/**
 * The scenarios of one project, newest first. Rows written before E16-T5 carry no `projectId`;
 * they are **adopted** by the first project that lists them rather than being orphaned — losing
 * someone's saved work to a storage-format change is not an acceptable upgrade.
 */
export async function listTemplates(projectId?: string | null): Promise<Template[]> {
  const all = (await withStore<Template[]>('readonly', (s) => s.getAll())) ?? [];
  const mine = scope(projectId);

  const legacy = all.filter((t) => t.projectId === undefined);
  for (const t of legacy) {
    t.projectId = mine;
    await withStore('readwrite', (s) => s.put(t));
  }

  return all.filter((t) => t.projectId === mine).sort((a, b) => b.createdAt - a.createdAt);
}

export async function deleteTemplate(id: string): Promise<void> {
  await withStore('readwrite', (s) => s.delete(id));
}

// ---- Workspace autosave (one "current" record per project) ----------------

/** The pre-E16-T5 key: a single workspace shared by every project. Adopted once, then removed. */
const LEGACY_WORKSPACE_KEY = 'current';

const workspaceKey = (projectId?: string | null) => `${LEGACY_WORKSPACE_KEY}:${scope(projectId)}`;

export async function saveWorkspace(data: unknown, projectId?: string | null): Promise<void> {
  await writeWorkspace(workspaceKey(projectId), data);
}

export async function loadWorkspace<T>(projectId?: string | null): Promise<T | undefined> {
  const own = await readWorkspace<T>(workspaceKey(projectId));
  if (own !== undefined) return own;

  // First open since the upgrade: hand the old shared workspace to whichever project asks first,
  // then delete it so a second project cannot inherit the same canvas.
  const legacy = await readWorkspace<T>(LEGACY_WORKSPACE_KEY);
  if (legacy === undefined) return undefined;

  await saveWorkspace(legacy, projectId);
  await deleteWorkspace(LEGACY_WORKSPACE_KEY);
  return legacy;
}

async function writeWorkspace(key: string, data: unknown): Promise<void> {
  const db = await openDb();
  await new Promise<void>((resolve, reject) => {
    const tx = db.transaction(WORKSPACE, 'readwrite');
    tx.objectStore(WORKSPACE).put(data, key);
    tx.oncomplete = () => {
      db.close();
      resolve();
    };
    tx.onerror = () => reject(tx.error);
  });
}

async function readWorkspace<T>(key: string): Promise<T | undefined> {
  const db = await openDb();
  return new Promise<T | undefined>((resolve, reject) => {
    const tx = db.transaction(WORKSPACE, 'readonly');
    const req = tx.objectStore(WORKSPACE).get(key);
    req.onsuccess = () => resolve(req.result as T | undefined);
    req.onerror = () => reject(req.error);
    tx.oncomplete = () => db.close();
  });
}

async function deleteWorkspace(key: string): Promise<void> {
  const db = await openDb();
  await new Promise<void>((resolve, reject) => {
    const tx = db.transaction(WORKSPACE, 'readwrite');
    tx.objectStore(WORKSPACE).delete(key);
    tx.oncomplete = () => {
      db.close();
      resolve();
    };
    tx.onerror = () => reject(tx.error);
  });
}
