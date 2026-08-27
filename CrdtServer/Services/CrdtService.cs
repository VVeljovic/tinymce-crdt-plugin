using Grpc.Core;
using Microsoft.AspNetCore.SignalR;

namespace CrdtServer.Services
{
    public class CrdtService : CrdtServer.CrdtService.CrdtServiceBase
    {
        private readonly CrdtDocumentStore _store;
        private readonly ILogger<CrdtService> _logger;
        private readonly IHubContext<CrdtHub> _hubContext;

        public CrdtService(CrdtDocumentStore store, ILogger<CrdtService> logger, IHubContext<CrdtHub> hubContext)
        {
            _store = store;
            _logger = logger;
            _hubContext = hubContext;
        }

        public override async Task InsertElement(
            IAsyncStreamReader<InsertElementMessage> requestStream,
            IServerStreamWriter<InsertElementMessage> responseStream,
            ServerCallContext context)
        {
            await foreach (var message in requestStream.ReadAllAsync(context.CancellationToken))
            {
                if (string.IsNullOrEmpty(message.Value))
                {
                    _logger.LogWarning("Ignored InsertElement with empty value for doc '{DocId}'.", message.DocId);
                    continue;
                }

                var document = _store.GetOrCreate(message.DocId);
                document.Insert(document.NodeId, message.Value[0], message.VisibleIndex);

                await _hubContext.Clients.Group(message.DocId).SendAsync("ContentChanged", document.GetText());

                _logger.LogInformation(
                    "Applied peer insert '{Value}' at index {Index} on doc '{DocId}'.",
                    message.Value, message.VisibleIndex, message.DocId);
            }
        }

        public override async Task DeleteElement(
            IAsyncStreamReader<DeleteElementMessage> requestStream,
            IServerStreamWriter<DeleteElementMessage> responseStream,
            ServerCallContext context)
        {
            await foreach (var message in requestStream.ReadAllAsync(context.CancellationToken))
            {
                var document = _store.GetOrCreate(message.DocId);
                document.Delete(message.VisibleIndex);

                await _hubContext.Clients.Group(message.DocId).SendAsync("ContentChanged", document.GetText());

                _logger.LogInformation(
                    "Applied peer delete at index {Index} on doc '{DocId}'.",
                    message.VisibleIndex, message.DocId);
            }
        }
    }
}
