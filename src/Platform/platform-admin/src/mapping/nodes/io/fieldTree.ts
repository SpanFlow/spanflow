// Turn a flat list of dotted field paths (orders, orders.id, orders.amount,
// user.name) into an indented tree, and edit it by path.

/** Sentinel handle id for an array-root target's parent (drives iteration). */
export const ROOT_HANDLE = '$root';

export interface FlatField {
  segment: string;   // the last path segment (e.g. "amount")
  path: string;      // the full dotted path / handle id (e.g. "orders.amount")
  connectable: boolean; // true when `path` is itself one of the fields (has a handle)
  depth: number;     // indentation level
  hasChildren: boolean; // true when this node has nested fields
}

interface TreeNode {
  segment: string;
  path: string;
  connectable: boolean;
  children: TreeNode[];
}

/** Build a depth-annotated, tree-ordered list from dotted field paths. */
export function buildFieldTree(fields: string[]): FlatField[] {
  const fieldSet = new Set(fields);
  const roots: TreeNode[] = [];
  const index = new Map<string, TreeNode>();

  for (const field of fields) {
    let path = '';
    let siblings = roots;
    for (const segment of field.split('.')) {
      path = path ? `${path}.${segment}` : segment;
      let node = index.get(path);
      if (!node) {
        node = { segment, path, connectable: fieldSet.has(path), children: [] };
        index.set(path, node);
        siblings.push(node);
      }
      siblings = node.children;
    }
  }

  const flat: FlatField[] = [];
  const walk = (nodes: TreeNode[], depth: number) => {
    for (const node of nodes) {
      flat.push({
        segment: node.segment,
        path: node.path,
        connectable: node.connectable,
        depth,
        hasChildren: node.children.length > 0,
      });
      walk(node.children, depth + 1);
    }
  };
  walk(roots, 0);
  return flat;
}

/** Rename a path's last segment, updating the node and all its descendants. */
export function renamePath(fields: string[], oldPath: string, newSegment: string): string[] {
  const parts = oldPath.split('.');
  parts[parts.length - 1] = newSegment;
  const newPath = parts.join('.');
  return fields.map((f) =>
    f === oldPath ? newPath : f.startsWith(`${oldPath}.`) ? newPath + f.slice(oldPath.length) : f,
  );
}

/** Remove a path and everything nested under it. */
export function removePath(fields: string[], path: string): string[] {
  return fields.filter((f) => f !== path && !f.startsWith(`${path}.`));
}
