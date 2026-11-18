import { describe, it, expect } from 'vitest';
import { readGuides, writeGuides } from './ediGuides';

describe('the edi guides option', () => {
  it('reads every form the service takes', () => {
    expect(readGuides(undefined)).toEqual({ all: false, names: [], own: [] });
    expect(readGuides('bundled')).toEqual({ all: true, names: [], own: [] });
    expect(readGuides('x12-850')).toEqual({ all: false, names: ['x12-850'], own: [] });
    expect(readGuides([{ name: 'acme' }, 'bundled'])).toEqual({ all: true, names: [], own: [{ name: 'acme' }] });
  });

  it('writes the shortest form, own guides first, nothing picked as unset', () => {
    expect(writeGuides({ all: false, names: [], own: [] })).toBeUndefined();
    expect(writeGuides({ all: true, names: ['x12-850'], own: [] })).toBe('bundled');
    expect(writeGuides({ all: false, names: ['x12-850', 'x12-997'], own: [] })).toEqual(['x12-850', 'x12-997']);
    expect(writeGuides({ all: true, names: [], own: [{ name: 'acme' }] })).toEqual([{ name: 'acme' }, 'bundled']);
  });
});
