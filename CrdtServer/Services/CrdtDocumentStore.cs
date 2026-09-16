using CrdtCore;
using System.Collections.Concurrent;
using System.Text.Json;

namespace CrdtServer.Services
{
    public class CrdtDocumentStore : IDisposable
    {
        private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);

        private readonly ConcurrentDictionary<string, CrdtDocument> _documents = new();
        private readonly ConcurrentDictionary<string, byte> _dirtyDocIds = new();
        private readonly string _dataDirectory = "Data";
        private readonly Timer _flushTimer;

        public CrdtDocumentStore(IHostApplicationLifetime lifetime)
        {
            _flushTimer = new Timer(_ => FlushDirty(), null, FlushInterval, FlushInterval);
            lifetime.ApplicationStopping.Register(FlushDirty);
        }

        public async Task<CrdtDocument> GetOrCreate(string docId)
        {
            if (_documents.TryGetValue(docId, out var document))
                return document;

            var path = Path.Combine(_dataDirectory, $"{docId}.json");

            if (File.Exists(path))
            {
                var json = await File.ReadAllTextAsync(path);

                if (string.IsNullOrEmpty(json))
                {
                    document = new CrdtDocument();
                }
                else
                {
                    document = JsonSerializer.Deserialize<CrdtDocument>(json)
                               ?? new CrdtDocument();
                }
            }
            else
            {
                document = new CrdtDocument();
            }

            _documents[docId] = document;

            return document;
        }

        public void MarkDirty(string docId) => _dirtyDocIds[docId] = 0;

        public async Task Save(string docId, CrdtDocument document)
        {
            Directory.CreateDirectory(_dataDirectory);

            var path = Path.Combine(_dataDirectory, $"{docId}.json");

            var json = JsonSerializer.Serialize(document);

            await File.WriteAllTextAsync(path, json);
        }

        private void FlushDirty()
        {
            foreach (var docId in _dirtyDocIds.Keys)
            {
                if (!_dirtyDocIds.TryRemove(docId, out _))
                {
                    continue;
                }

                if (!_documents.TryGetValue(docId, out var document))
                {
                    continue;
                }

                try
                {
                    Save(docId, document).GetAwaiter().GetResult();
                }
                catch
                {
                    _dirtyDocIds[docId] = 0;
                }
            }
        }

        public void Dispose() => _flushTimer.Dispose();
    }
}
