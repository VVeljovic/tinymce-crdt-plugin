using CrdtCore;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CrdtServer.Services
{
    public class CrdtDocumentStore
    {
        private readonly ConcurrentDictionary<string, CrdtDocument> _documents = new();
        private readonly string _dataDirectory;

        public CrdtDocumentStore(IConfiguration configuration)
        {
            var instanceId = configuration["RabbitMq:InstanceId"] ?? "default";
            _dataDirectory = $"Data-{instanceId}";
        }

        public async Task<CrdtDocument> GetOrCreate(string docId)
        {
            if (_documents.TryGetValue(docId, out var document))
                return document;

            var path = Path.Combine(_dataDirectory, $"{docId}.json");

            if (File.Exists(path))
            {
                var json = await File.ReadAllTextAsync(path);

                document = string.IsNullOrEmpty(json)
                    ? new CrdtDocument()
                    : JsonSerializer.Deserialize<CrdtDocument>(json) ?? new CrdtDocument();
            }
            else
            {
                document = new CrdtDocument();
            }

            _documents[docId] = document;

            return document;
        }

        public async Task Save(string docId, CrdtDocument document)
        {
            Directory.CreateDirectory(_dataDirectory);

            var path = Path.Combine(_dataDirectory, $"{docId}.json");

            string json;
            lock (document)
            {
                json = JsonSerializer.Serialize(document);
            }

            await File.WriteAllTextAsync(path, json);
        }
    }
}
