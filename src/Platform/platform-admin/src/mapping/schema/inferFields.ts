// Infer the field structure of a sample JSON document, as dotted paths that the
// node graph and compiler understand (e.g. "orders", "orders.amount",
// "user.name"). For a list of objects we emit the list path itself AND its
// element leaves, so the tree shows the parent (the list) with its children.

const isObject = (v: unknown): v is Record<string, unknown> =>
  typeof v === 'object' && v !== null && !Array.isArray(v);

/** The JSON type shown on a field badge. */
export type FieldType = 'string' | 'number' | 'boolean' | 'null' | 'object' | 'array';

const scalarType = (v: unknown): FieldType =>
  v === null ? 'null' : (typeof v as 'string' | 'number' | 'boolean');

function walk(node: unknown, prefix: string, fields: string[], lists: string[], types: Record<string, FieldType>) {
  if (Array.isArray(node)) {
    if (prefix) {
      fields.push(prefix); // the list itself (a connectable parent)
      lists.push(prefix);  // and remember it is an array
      types[prefix] = 'array';
    }
    if (node.length > 0 && isObject(node[0])) {
      walk(node[0], prefix, fields, lists, types); // element leaves keep the list's prefix
    }
    return;
  }
  if (isObject(node)) {
    // Don't clobber an array parent: a list of objects already set this to 'array'.
    if (prefix && !(prefix in types)) types[prefix] = 'object';
    for (const [key, value] of Object.entries(node)) {
      walk(value, prefix ? `${prefix}.${key}` : key, fields, lists, types);
    }
    return;
  }
  if (prefix) {
    fields.push(prefix); // primitive leaf
    types[prefix] = scalarType(node);
  }
}

/** Field paths, the subset that are arrays (lists), a path→type map, and whether the root is an array. */
export function inferStructure(json: unknown): {
  fields: string[];
  lists: string[];
  types: Record<string, FieldType>;
  rootArray: boolean;
} {
  const fields: string[] = [];
  const lists: string[] = [];
  const types: Record<string, FieldType> = {};
  walk(json, '', fields, lists, types);
  return { fields: [...new Set(fields)], lists: [...new Set(lists)], types, rootArray: Array.isArray(json) };
}

export const inferSourceFields = (json: unknown): string[] => inferStructure(json).fields;
export const inferTargetFields = (json: unknown): string[] => inferStructure(json).fields;
