const databaseName = 'qsock-module-cache';

export async function verifyPluginSignature(publisher, payload, signature) {
  const bytes = (value) => Uint8Array.from(atob(value), (character) => character.charCodeAt(0));
  const key = await crypto.subtle.importKey('spki', bytes(publisher), { name: 'ECDSA', namedCurve: 'P-256' }, false, ['verify']);
  return crypto.subtle.verify({ name: 'ECDSA', hash: 'SHA-256' }, key, bytes(signature), bytes(payload));
}

async function database() {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(databaseName, 1);
    request.onupgradeneeded = () => request.result.createObjectStore('packages');
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}

async function operation(mode, action) {
  const db = await database();
  try {
    return await new Promise((resolve, reject) => {
      const transaction = db.transaction('packages', mode);
      const request = action(transaction.objectStore('packages'));
      let result;
      if (request) request.onsuccess = () => { result = request.result; };
      transaction.oncomplete = () => resolve(result);
      transaction.onerror = () => reject(transaction.error);
      transaction.onabort = () => reject(transaction.error || Error('Кэш модулей недоступен.'));
    });
  } finally {
    db.close();
  }
}

export const pluginCache = {
  read: async (server, key) => (await operation('readonly', (store) => store.get(server + '/' + key))) || '',
  write: async (server, key, bytes) => { await operation('readwrite', (store) => store.put(bytes, server + '/' + key)); },
  prune: async (server, keepJson) => {
    const keep = new Set(JSON.parse(keepJson).map((key) => server + '/' + key));
    await operation('readwrite', (store) => {
      const request = store.openKeyCursor(IDBKeyRange.bound(server + '/', server + '/\uffff'));
      request.onsuccess = () => {
        const cursor = request.result;
        if (!cursor) return;
        if (!keep.has(cursor.primaryKey)) store.delete(cursor.primaryKey);
        cursor.continue();
      };
    });
  },
};
