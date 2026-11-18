import { describe, it, expect } from 'vitest';
import { mergeLists, repeatedPaths } from './xmlHints';

describe('repeatedPaths', () => {
  it('lists elements seen more than once under one parent, at any depth, with prefixes', () => {
    const xml = `<o:Order xmlns:o="urn:o"><o:Line><o:Tag/><o:Tag/></o:Line><o:Line/><o:Note/></o:Order>`;
    expect(repeatedPaths(xml)).toEqual(['/o:Order/o:Line', '/o:Order/o:Line/o:Tag']);
  });

  it('lists nothing for a sample with no repeats, and null for broken XML', () => {
    expect(repeatedPaths('<a><b/><c/></a>')).toEqual([]);
    expect(repeatedPaths('<a><b></a>')).toBeNull();
  });

  it('merges without repeats, keeping what was typed first', () => {
    expect(mergeLists(['Line'], ['/Order/Line', 'Line'])).toEqual(['Line', '/Order/Line']);
  });
});
