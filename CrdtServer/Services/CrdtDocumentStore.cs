using System.Collections.Concurrent;
using CrdtCore;

namespace CrdtServer.Services
{
    /// <summary>
    /// Shared in-memory document state, keyed by docId.
    /// Used by both the SignalR hub (browser clients) and the gRPC service (peer servers)
    /// so they operate on the same CrdtDocument instances.
    /// </summary>
    public class CrdtDocumentStore
    {
        private readonly ConcurrentDictionary<string, CrdtDocument> _documents = new();

        public CrdtDocument GetOrCreate(string docId) =>
            _documents.GetOrAdd(docId, _ => new CrdtDocument());
    }
}
