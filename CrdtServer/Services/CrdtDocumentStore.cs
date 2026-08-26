using System.Collections.Concurrent;
using CrdtCore;

namespace CrdtServer.Services
{
    public class CrdtDocumentStore
    {
        private readonly ConcurrentDictionary<string, CrdtDocument> _documents = new();

        public CrdtDocument GetOrCreate(string docId) =>
            _documents.GetOrAdd(docId, _ => new CrdtDocument());
    }
}
