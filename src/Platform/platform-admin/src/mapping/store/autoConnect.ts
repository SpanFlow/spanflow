import type { Connection, Edge, Node } from '@xyflow/react';

/**
 * When a Source field is connected to a Target field and both have descendants,
 * produce connections for descendants whose relative path matches on both sides
 * (e.g. wiring source `orders` to target `orders` auto-maps `id`->`id`,
 * `amount`->`amount`). Like Altova MapForce's parent-to-parent auto-connect.
 */
export function matchChildConnections(connection: Connection, nodes: Node[], existing: Edge[]): Connection[] {
  const source = nodes.find((n) => n.id === connection.source);
  const target = nodes.find((n) => n.id === connection.target);
  if (source?.type !== 'source' || target?.type !== 'target') return [];

  const sourceParent = connection.sourceHandle ?? '';
  const targetParent = connection.targetHandle ?? '';
  if (!sourceParent || !targetParent) return [];

  const sourceFields = (source.data?.fields as string[]) ?? [];
  const targetFields = (target.data?.fields as string[]) ?? [];

  // Map a target descendant's relative path -> its full handle.
  const targetByRel = new Map<string, string>();
  for (const f of targetFields) {
    if (f.startsWith(`${targetParent}.`)) targetByRel.set(f.slice(targetParent.length + 1), f);
  }

  const out: Connection[] = [];
  for (const f of sourceFields) {
    if (!f.startsWith(`${sourceParent}.`)) continue;
    const rel = f.slice(sourceParent.length + 1);
    const targetFull = targetByRel.get(rel);
    if (!targetFull) continue;

    const exists = existing.some(
      (e) => e.source === source.id && e.sourceHandle === f && e.target === target.id && e.targetHandle === targetFull,
    );
    if (exists) continue;

    out.push({ source: source.id, sourceHandle: f, target: target.id, targetHandle: targetFull });
  }
  return out;
}
