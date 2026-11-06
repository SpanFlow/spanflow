import { useStore } from './useStore';
import { dslToGraph } from '../nodes/decompile';
import { groupsOf, storedGraph } from './graphGroups';

const FILE_VERSION = 1;

/** Download the current graph (nodes + edges) as a .maplite.json file. */
export function savePipeline(): void {
  const { nodes, edges, groups } = useStore.getState();
  const data = JSON.stringify({ version: FILE_VERSION, ...storedGraph(nodes, edges, groups) }, null, 2);
  const blob = new Blob([data], { type: 'application/json' });
  const url = URL.createObjectURL(blob);

  const a = document.createElement('a');
  a.href = url;
  a.download = 'pipeline.maplite.json';
  a.click();
  URL.revokeObjectURL(url);
}

/**
 * Load either a saved graph (from {@link savePipeline}) or a raw mapping DSL
 * (`{ target: { mappings: [...] } }`), which is reverse-compiled into a graph.
 */
export function loadPipelineFromText(text: string): void {
  const parsed = JSON.parse(text);

  let nodes: unknown;
  let edges: unknown;
  // A raw DSL is reverse-compiled into fresh nodes, so it has no groups to keep (E61-T5).
  let groups = groupsOf(null);
  if (Array.isArray(parsed?.nodes) && Array.isArray(parsed?.edges)) {
    nodes = parsed.nodes;
    edges = parsed.edges;
    groups = groupsOf(parsed);
  } else if (Array.isArray(parsed?.target?.mappings)) {
    const graph = dslToGraph(parsed);
    nodes = graph.nodes;
    edges = graph.edges;
  } else {
    throw new Error('Not a pipeline (nodes/edges) or a mapping DSL (target.mappings).');
  }

  useStore.getState().pushHistory();
  useStore.getState().setNodes(nodes as never);
  useStore.getState().setEdges(edges as never);
  useStore.setState({ groups });
}
