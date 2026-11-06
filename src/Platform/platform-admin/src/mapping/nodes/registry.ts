import type { ComponentType } from 'react';
import type { NodeGroup, NodeSpec } from './base';
import { withNodeDebug } from './withNodeDebug';
import { sourceSpec } from './io/source/spec';
import { targetSpec } from './io/target/spec';
import { aggregateSpec } from './functions/aggregate/spec';
import { valueMapSpec } from './functions/valueMap/spec';
import { sortSpec } from './functions/sort/spec';
import { transformSpec } from './functions/transform/spec';
import { filterSpec } from './functions/filter/spec';
import { foreachSpec } from './collections/foreach/spec';
import { groupBySpec } from './collections/groupBy/spec';
import { joinSpec } from './collections/join/spec';
import { ifElseSpec } from './logic/condition/ifElse/spec';
import { whereSpec } from './logic/where/spec';
import { constantSpec } from './logic/constant/spec';
import { exceptionSpec } from './logic/exception/spec';
import { paramSpec, returnSpec, functionCallSpec } from './function/specs';

/** Every node, in palette order. Add a node by adding its spec here. */
export const specs: NodeSpec[] = [
  sourceSpec,
  targetSpec,
  aggregateSpec,
  transformSpec,
  valueMapSpec,
  sortSpec,
  filterSpec,
  foreachSpec,
  groupBySpec,
  joinSpec,
  ifElseSpec,
  whereSpec,
  exceptionSpec,
  constantSpec,
  paramSpec,
  returnSpec,
  functionCallSpec,
];

export const specByType: Record<string, NodeSpec> = Object.fromEntries(
  specs.map((s) => [s.type, s]),
);

/** React Flow node component map (each wrapped with a debug footer). */
export const nodeTypes: Record<string, ComponentType<any>> = Object.fromEntries(
  specs.map((s) => [s.type, withNodeDebug(s.component)]),
);

/** Default data for a freshly-dropped node of the given type. */
export function defaultNodeData(type: string): Record<string, any> {
  return specByType[type]?.defaultData() ?? {};
}

const GROUP_ORDER: { id: NodeGroup; title: string }[] = [
  { id: 'io', title: 'I/O' },
  { id: 'functions', title: 'Functions' },
  { id: 'collections', title: 'Collections' },
  { id: 'logic', title: 'Logic' },
];

/** Sidebar palette, grouped and ordered. */
export const sidebarGroups = GROUP_ORDER.map((g) => ({
  title: g.title,
  items: specs.filter((s) => s.group === g.id),
}));
