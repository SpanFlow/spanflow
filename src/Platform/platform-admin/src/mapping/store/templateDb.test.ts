import { describe, it, expect, beforeEach } from 'vitest'
import 'fake-indexeddb/auto'
import { IDBFactory } from 'fake-indexeddb'
import {
  saveWorkspace,
  loadWorkspace,
  saveTemplate,
  listTemplates,
  deleteTemplate,
} from './templateDb'

/**
 * E16-T5. The designer's browser storage used to be global: one workspace record keyed 'current'
 * and one undifferentiated pile of scenarios. Opening the designer in another project therefore
 * restored the previous project's canvas and then autosaved over it — the bug these tests pin.
 */

const A = 'project-a'
const B = 'project-b'

beforeEach(() => {
  // A fresh factory per test: IndexedDB is process-global otherwise, and version upgrades
  // would leak between cases.
  globalThis.indexedDB = new IDBFactory()
})

const node = (id: string) => ({ id, type: 'source', position: { x: 0, y: 0 }, data: {} }) as never

describe('scenario groups (E61-T5)', () => {
  it('keeps a scenario’s node groups, and stores none when it has none', async () => {
    const groups = [{ id: 'g1', name: 'Lookup', nodeIds: ['a', 'b'], collapsed: true }]
    await saveTemplate('grouped', [node('a'), node('b')], [], '{}', A, groups)
    await saveTemplate('plain', [node('a')], [], '{}', A)

    const saved = await listTemplates(A)
    expect(saved.find((t) => t.name === 'grouped')!.groups).toEqual(groups)
    expect('groups' in saved.find((t) => t.name === 'plain')!).toBe(false)
  })
})

describe('workspace autosave', () => {
  it('keeps a separate canvas per project', async () => {
    await saveWorkspace({ docs: ['a'] }, A)
    await saveWorkspace({ docs: ['b'] }, B)

    expect(await loadWorkspace(A)).toEqual({ docs: ['a'] })
    expect(await loadWorkspace(B)).toEqual({ docs: ['b'] })
  })

  it('returns nothing for a project that has never saved one', async () => {
    await saveWorkspace({ docs: ['a'] }, A)
    expect(await loadWorkspace(B)).toBeUndefined()
  })

  it('gives a project with no id its own bucket rather than sharing one', async () => {
    await saveWorkspace({ docs: ['none'] }, undefined)
    expect(await loadWorkspace(undefined)).toEqual({ docs: ['none'] })
    expect(await loadWorkspace(A)).toBeUndefined()
  })

  it('adopts a pre-E16-T5 workspace exactly once, so nobody loses in-progress work', async () => {
    // Simulate the old format: a record under the bare 'current' key.
    await writeLegacyWorkspace({ docs: ['legacy'] })

    expect(await loadWorkspace(A)).toEqual({ docs: ['legacy'] })
    // Now it belongs to A alone — a second project must not inherit the same canvas.
    expect(await loadWorkspace(B)).toBeUndefined()
    expect(await loadWorkspace(A)).toEqual({ docs: ['legacy'] })
  })
})

describe('saved scenarios', () => {
  it("lists only the current project's scenarios", async () => {
    await saveTemplate('a-one', [node('n1')], [], '{}', A)
    await saveTemplate('b-one', [node('n2')], [], '{}', B)

    expect((await listTemplates(A)).map((t) => t.name)).toEqual(['a-one'])
    expect((await listTemplates(B)).map((t) => t.name)).toEqual(['b-one'])
  })

  it('adopts pre-E16-T5 scenarios into the first project that lists them', async () => {
    await writeLegacyTemplate('tpl-legacy', 'old scenario')

    expect((await listTemplates(A)).map((t) => t.name)).toEqual(['old scenario'])
    expect(await listTemplates(B)).toEqual([])
  })

  it('deletes by id', async () => {
    const t = await saveTemplate('gone', [node('n1')], [], '{}', A)
    await deleteTemplate(t.id)
    expect(await listTemplates(A)).toEqual([])
  })
})

// --- helpers that write the *old* on-disk shapes, so the migration paths are really exercised ---

function openDb(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const req = indexedDB.open('maplite', 3)
    req.onupgradeneeded = () => {
      const db = req.result
      if (!db.objectStoreNames.contains('templates')) db.createObjectStore('templates', { keyPath: 'id' })
      if (!db.objectStoreNames.contains('workspace')) db.createObjectStore('workspace')
    }
    req.onsuccess = () => resolve(req.result)
    req.onerror = () => reject(req.error)
  })
}

async function writeLegacyWorkspace(data: unknown): Promise<void> {
  const db = await openDb()
  await new Promise<void>((resolve, reject) => {
    const tx = db.transaction('workspace', 'readwrite')
    tx.objectStore('workspace').put(data, 'current')
    tx.oncomplete = () => {
      db.close()
      resolve()
    }
    tx.onerror = () => reject(tx.error)
  })
}

async function writeLegacyTemplate(id: string, name: string): Promise<void> {
  const db = await openDb()
  await new Promise<void>((resolve, reject) => {
    const tx = db.transaction('templates', 'readwrite')
    // No projectId — the shape written before E16-T5.
    tx.objectStore('templates').put({ id, name, createdAt: Date.now(), nodes: [], edges: [], sampleInput: '{}' })
    tx.oncomplete = () => {
      db.close()
      resolve()
    }
    tx.onerror = () => reject(tx.error)
  })
}
