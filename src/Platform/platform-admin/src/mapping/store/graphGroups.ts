import { cleanGroups, type NodeGroup } from '../../components/canvas/groups/nodeGroups';

/**
 * The designer's graph as it is stored — on the server beside the compiled definition, in a saved
 * scenario, in a downloaded file — with its node groups (E61-T5) cleaned against the nodes and left out
 * when there are none, so a canvas nobody grouped stores exactly what it always did.
 */
export function storedGraph<N extends { id: string }, E>(nodes: N[], edges: E[], groups: readonly NodeGroup[] | null | undefined) {
  const clean = cleanGroups(groups, nodes.map((n) => n.id));
  return { nodes, edges, ...(clean.length ? { groups: clean } : {}) };
}

/** The groups a stored graph carries, if any. Anything unreadable is no groups, never an error. */
export function groupsOf(graph: unknown): NodeGroup[] {
  const groups = (graph as { groups?: unknown } | null | undefined)?.groups;
  return Array.isArray(groups) ? (groups as NodeGroup[]) : [];
}
