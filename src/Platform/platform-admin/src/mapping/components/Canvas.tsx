import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  ReactFlow,
  Background,
  Controls,
  MiniMap,
  Panel,
  ReactFlowProvider,
  BackgroundVariant,
  useReactFlow
} from '@xyflow/react';
import type { Connection, Edge } from '@xyflow/react';
import '@xyflow/react/dist/style.css';
import { Boxes, Map, MapPinned } from 'lucide-react';
import { useStore, type AppNode } from '../store/useStore';
import { nodeTypes as mappingNodeTypes } from '../nodes/registry';
import { cleanGroups, isGroupNodeId, useCanvasGroups, GROUP_NODE_TYPES, type GroupRunState } from '../../components/canvas/groups';
import { canConnect } from '../nodes/connect';
import { NodeHelpModal } from './NodeHelp';
import { LearnMore } from '../../components/HelpDrawer';

const nodeTypes = { ...mappingNodeTypes, ...GROUP_NODE_TYPES };

/** Mapping canvases keep their positions, so a group never changes where anything sits (D61.5). */
const keepPositions = () => {};

const CanvasFlow = () => {
  const { nodes, edges, onNodesChange, onEdgesChange, onConnect, addNode, pushHistory, theme } = useStore();
  const storedGroups = useStore((s) => s.groups);
  const setGroups = useStore((s) => s.setGroups);
  const debug = useStore((s) => s.debug);
  const [hint, setHint] = useState<string | null>(null);
  const { screenToFlowPosition, fitView } = useReactFlow();
  const fitNonce = useStore((s) => s.fitNonce);
  const [showMiniMap, setShowMiniMap] = useState(true);

  // ---- node groups (E61-T5) ----
  // Kept with the document's graph (tabs, undo, autosave, scenarios, the saved mapping) and cleaned
  // against the nodes here, so a deleted member never leaves a group pointing at nothing.
  const nodeIdsKey = nodes.map((n) => n.id).join('|');
  const groups = useMemo(() => cleanGroups(storedGroups, nodeIdsKey ? nodeIdsKey.split('|') : []), [storedGroups, nodeIdsKey]);

  /** A folded group's members summed from the last debug run (D61.8). */
  const runStateOf = useCallback(
    (members: AppNode[]): GroupRunState | undefined => {
      if (!debug.active) return undefined;
      if (debug.running) return { running: members.length, failed: 0, succeeded: 0 };
      const results = members.map((n) => debug.results[n.id]).filter(Boolean);
      if (results.length === 0) return undefined;
      return { running: 0, failed: results.filter((r) => r.error).length, succeeded: results.filter((r) => !r.error).length };
    },
    [debug],
  );

  const setNodesWith = useCallback(
    (update: (current: AppNode[]) => AppNode[]) => useStore.getState().setNodes(update(useStore.getState().nodes)),
    [],
  );

  const grouping = useCanvasGroups({
    enabled: true,
    groups,
    onGroupsChange: setGroups,
    relayout: keepPositions,
    nodes,
    edges,
    setNodes: setNodesWith,
    onNodesChange,
    runStateOf,
    onSelectNode: () => {},
    onWarning: setHint,
  });

  // After a load/import, fit the view so newly placed nodes (e.g. a Target at
  // the far right) are actually on screen.
  useEffect(() => {
    if (fitNonce === 0) return;
    const id = requestAnimationFrame(() => fitView({ duration: 300, padding: 0.2 }));
    return () => cancelAnimationFrame(id);
  }, [fitNonce, fitView]);

  // Port rules live in canConnect so they can be tested without a canvas.
  const isValidConnection = useCallback(
    (c: Connection | Edge) => {
      const source = nodes.find((n) => n.id === c.source);
      const target = nodes.find((n) => n.id === c.target);
      if (!source || !target) return false;
      return canConnect(source.type as string, target.type as string, c.targetHandle);
    },
    [nodes],
  );

  const onDragOver = useCallback((event: React.DragEvent) => {
    event.preventDefault();
    event.dataTransfer.dropEffect = 'move';
  }, []);

  const onDrop = useCallback(
    (event: React.DragEvent) => {
      event.preventDefault();

      const raw = event.dataTransfer.getData('application/reactflow');
      if (!raw) return;

      // Convert the drop point from screen space into flow space so the node
      // lands under the cursor regardless of the current pan/zoom (fitView).
      const position = screenToFlowPosition({
        x: event.clientX,
        y: event.clientY,
      });

      // A custom-function drop is encoded as "functionCall:<docId>".
      if (raw.startsWith('functionCall:')) {
        addNode('functionCall', position, { functionId: raw.slice('functionCall:'.length) });
      } else {
        addNode(raw, position);
      }
    },
    [addNode, screenToFlowPosition]
  );

  return (
    <div className="flex-1 min-h-0 w-full relative" ref={grouping.canvasRef} onKeyDown={grouping.onKeyDown}>
      <NodeHelpModal />
      {grouping.menuElement}
      <ReactFlow
        // The nodes and edges as drawn, with groups folded or framed, and the group-aware node handlers.
        {...grouping.flowProps}
        onEdgesChange={onEdgesChange}
        onConnect={onConnect}
        isValidConnection={isValidConnection}
        onNodeDragStart={() => pushHistory()}
        onNodeClick={(_, node) => { grouping.closeMenu(); if (isGroupNodeId(node.id)) setHint(null); }}
        onPaneClick={() => { grouping.closeMenu(); setHint(null); }}
        onDrop={onDrop}
        onDragOver={onDragOver}
        nodeTypes={nodeTypes}
        deleteKeyCode={['Backspace', 'Delete']}
        fitView
        colorMode={theme}
      >
        <Background variant={BackgroundVariant.Dots} gap={16} size={1} className="dark:bg-slate-900 bg-slate-50 transition-colors" />
        <Controls className="fill-slate-700 dark:fill-slate-300" />
        {hint && (
          <Panel position="top-left">
            <div role="status" className="rounded-md border border-amber-200 bg-amber-50 px-2 py-1 text-xs text-amber-800 shadow-sm dark:border-amber-900 dark:bg-amber-950 dark:text-amber-200">
              {hint}
            </div>
          </Panel>
        )}
        <Panel position="top-right" className="flex gap-1">
          <button
            onClick={grouping.groupSelected}
            disabled={grouping.selectedNodeIds.length < 2}
            className="flex items-center gap-1 px-2 py-1.5 rounded-md bg-white/90 dark:bg-slate-800/90 border border-slate-200 dark:border-slate-700 text-xs text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-700 shadow-sm transition-colors disabled:opacity-50"
            title={grouping.selectedNodeIds.length < 2
              ? 'Select two or more nodes to group them (Shift-drag a box round them)'
              : `Fold the ${grouping.selectedNodeIds.length} selected nodes into one named card (Ctrl+G)`}
          >
            <Boxes size={14} /> Group
          </button>
          <span className="flex items-center rounded-md bg-white/90 px-1.5 shadow-sm dark:bg-slate-800/90">
            <LearnMore slug="concepts/group-nodes" label="Groups" />
          </span>
          <button
            onClick={() => setShowMiniMap((v) => !v)}
            className="p-1.5 rounded-md bg-white/90 dark:bg-slate-800/90 border border-slate-200 dark:border-slate-700 text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-700 shadow-sm transition-colors"
            title={showMiniMap ? 'Hide node map' : 'Show node map'}
          >
            {showMiniMap ? <MapPinned size={16} /> : <Map size={16} />}
          </button>
        </Panel>
        {showMiniMap && (
          <MiniMap
            pannable
            zoomable
            ariaLabel="Node map: drag to navigate"
            nodeColor={(n) => (n.type === 'source' ? '#3b5bfd' : n.type === 'target' ? '#a855f7' : '#9aa5c0')}
            className="!bg-slate-100/90 dark:!bg-slate-800/90 rounded-md border border-slate-200 dark:border-slate-700"
          />
        )}
      </ReactFlow>
    </div>
  );
};

export const Canvas = () => (
  <ReactFlowProvider>
    <CanvasFlow />
  </ReactFlowProvider>
);
