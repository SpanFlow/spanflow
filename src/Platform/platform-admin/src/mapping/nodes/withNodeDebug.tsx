import type { ComponentType } from 'react';
import { NodeDebug } from './NodeDebug';

/** Wrap a node component so it renders a debug footer (request/response/timing). */
export function withNodeDebug(Component: ComponentType<any>): ComponentType<any> {
  const Wrapped = (props: any) => (
    <>
      <Component {...props} />
      <NodeDebug nodeId={props.id} />
    </>
  );
  Wrapped.displayName = `withNodeDebug(${Component.displayName || Component.name || 'Node'})`;
  return Wrapped;
}
