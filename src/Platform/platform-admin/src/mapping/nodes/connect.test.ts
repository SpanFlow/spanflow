import { describe, it, expect } from 'vitest';
import { canConnect } from './connect';

describe('canConnect', () => {
  describe("Group By's where/having ports", () => {
    it('accepts a Where node', () => {
      expect(canConnect('where', 'groupBy', 'where')).toBe(true);
      expect(canConnect('where', 'groupBy', 'having')).toBe(true);
    });

    it('rejects any other node: a filter or an aggregate is not a condition', () => {
      expect(canConnect('filter', 'groupBy', 'where')).toBe(false);
      expect(canConnect('aggregate', 'groupBy', 'having')).toBe(false);
      expect(canConnect('source', 'groupBy', 'where')).toBe(false);
    });

    it('still accepts a collection on the ordinary `in` port', () => {
      expect(canConnect('source', 'groupBy', 'in')).toBe(true);
      expect(canConnect('filter', 'groupBy', 'in')).toBe(true);
    });
  });

  describe('the Where node produces a condition, not a value', () => {
    it('cannot be wired into a port that wants a value', () => {
      expect(canConnect('where', 'target', 'someField')).toBe(false);
      expect(canConnect('where', 'aggregate', 'in')).toBe(false);
      expect(canConnect('where', 'sort', 'in')).toBe(false);
    });

    it('takes no input of its own', () => {
      expect(canConnect('source', 'where', 'in')).toBe(false);
    });
  });

  describe('the pre-existing rules still hold', () => {
    it('a Target has no output', () => {
      expect(canConnect('target', 'groupBy', 'in')).toBe(false);
    });

    it('a Source accepts no input', () => {
      expect(canConnect('aggregate', 'source', 'in')).toBe(false);
    });

    it('an ordinary value connection is fine', () => {
      expect(canConnect('groupBy', 'target', 'customers')).toBe(true);
    });
  });
});
