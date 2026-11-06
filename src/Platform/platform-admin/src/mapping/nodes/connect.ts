import { specByType } from './registry';

/**
 * Whether a connection is allowed, from the node specs alone.
 *
 * Three rules, in order:
 *  - a node with no output (Target) cannot be a source;
 *  - a node that accepts no input (Source, Constant) cannot be a target;
 *  - a typed port takes only the node types it names, and a node whose output is
 *    not a value (Where) may only feed such a port.
 */
export function canConnect(
  sourceType: string | undefined,
  targetType: string | undefined,
  targetHandle: string | null | undefined,
): boolean {
  const sourceSpec = sourceType ? specByType[sourceType] : undefined;
  const targetSpec = targetType ? specByType[targetType] : undefined;

  if (sourceSpec && !sourceSpec.ports.output) return false;
  if (targetSpec && targetSpec.ports.inputs === 'none') return false;

  const allowed = targetSpec?.accepts?.[targetHandle ?? ''];
  const accepted = !!allowed && !!sourceType && allowed.includes(sourceType);

  if (allowed && !accepted) return false;
  if (sourceSpec?.outputOnlyToAccepting && !accepted) return false;

  return true;
}
