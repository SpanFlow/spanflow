import type { ComponentType } from 'react';
import type { LucideIcon } from 'lucide-react';
import type { CompileContext, Expr } from './compile-types';

/** Palette grouping for the sidebar. */
export type NodeGroup = 'io' | 'functions' | 'collections' | 'logic' | 'function';

/**
 * Port rules express the "prev / next" capability of a node:
 * - inputs: how many incoming (prev) connections the node accepts
 *     'none'     → no prev (Source, Constant)
 *     'one'      → a single input (Aggregate, Value-Map, Sort)
 *     'perField' → one per declared field (Target)
 *     'dynamic'  → a variable number (If/Else clauses + then/else)
 * - output: whether the node produces a value (next). Target has none.
 */
export interface PortRule {
  inputs: 'none' | 'one' | 'perField' | 'dynamic';
  output: boolean;
}

/**
 * Node documentation, shown in the help modal rather than on the node itself —
 * canvases stay readable, and there is room for a worked example.
 */
export interface NodeHelp {
  /** One line: what the node does. */
  summary: string;
  /** Usage notes, one bullet each. */
  details?: string[];
  /** A worked example: a snippet of the DSL this node compiles to. */
  example?: { caption: string; code: string };
}

/**
 * A NodeSpec is the single source of truth for a node type. Registering a spec
 * wires the React Flow component, the sidebar palette entry, and the default
 * data for newly-dropped nodes — so adding a node means adding one folder.
 */
export interface NodeSpec {
  type: string;
  group: NodeGroup;
  /** Sidebar palette label. */
  label: string;
  icon: LucideIcon;
  /** Tailwind color name (e.g. 'indigo'); see Sidebar color map. */
  color: string;
  component: ComponentType<any>;
  /** Initial data for a freshly-dropped node. */
  defaultData: () => Record<string, any>;
  ports: PortRule;
  /**
   * Per-target-handle whitelist of source node types. A handle listed here
   * accepts ONLY those types (e.g. Group By's `where` port takes a Where node
   * and nothing else). Handles not listed keep accepting anything.
   */
  accepts?: Record<string, string[]>;
  /**
   * When true, this node's output may only feed a handle that explicitly
   * accepts its type — a predicate like Where is not a value, so it must not be
   * droppable into a Target field or an Aggregate input.
   */
  outputOnlyToAccepting?: boolean;
  /** Shown in the help modal (the ⓘ on the node header), not on the node. */
  help?: NodeHelp;
  /**
   * Help that depends on the node's own settings — e.g. Aggregate and Transform
   * document the function currently selected. Falls back to `help` when absent.
   */
  helpFor?: (data: Record<string, any>) => NodeHelp | undefined;
  /** Compile this node's output into a nested DSL expression. */
  compile: (ctx: CompileContext) => Expr;
}
