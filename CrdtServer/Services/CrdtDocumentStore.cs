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
            // Each server instance persists to its own folder, named after the
            // port it listens on, so two server processes launched from the
            // same working directory never read/write the same physical file.
            var urls = configuration["urls"]
                ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS")
                ?? string.Empty;
            var portMatch = Regex.Match(urls, @":(\d+)");
            var port = portMatch.Success ? portMatch.Groups[1].Value : "default";

            _dataDirectory = $"Data-{port}";
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

            var json = JsonSerializer.Serialize(document);

            await File.WriteAllTextAsync(path, json);
        }
    }
}
