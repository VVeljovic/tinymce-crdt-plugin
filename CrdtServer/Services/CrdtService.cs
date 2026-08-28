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

        public override async Task InsertOperation(IAsyncStreamReader<InsertOperationMessage> requestStream,
            IServerStreamWriter<InsertOperationMessage> responseStream,
            ServerCallContext context)
        {
            await foreach (var message in requestStream.ReadAllAsync(context.CancellationToken))
            {
                if (string.IsNullOrEmpty(message.Element.Value))
                {
                    _logger.LogWarning("Ignored InsertElement with empty value for doc '{DocId}'.", message.DocId);
                    continue;
                }

                var document = _store.GetOrCreate(message.DocId);
                document.RemoteInsert(ToDomainElement(message.Element));

                await _hubContext.Clients.Group(message.DocId).SendAsync("ElementsChanged", document.Elements);

                _logger.LogInformation(
                    "Applied peer insert '{Value}' at index {Index} on doc '{DocId}'.",
                    message.Element.Value, message.Element.Id, message.DocId);
            }
        }

        public override async Task DeleteOperation(IAsyncStreamReader<DeleteOperationMessage> requestStream,
            IServerStreamWriter<DeleteOperationMessage> responseStream,
            ServerCallContext context)
        {
            await foreach (var message in requestStream.ReadAllAsync(context.CancellationToken))
            {
                var document = _store.GetOrCreate(message.DocId);
                document.RemoteDelete(ToDomainId(message.ElementId));

                await _hubContext.Clients.Group(message.DocId).SendAsync("ElementsChanged", document.Elements);

                _logger.LogInformation(
                    "Applied peer delete at index {Index} on doc '{DocId}'.",
                    message.ElementId, message.DocId);
            }
        }

        private static CrdtCore.CrdtId ToDomainId(CrdtId protoId) => new(protoId.NodeId, protoId.Counter);

        private static CrdtCore.CrdtElement ToDomainElement(CrdtElement protoElement) => new CrdtCore.CrdtElement
        {
            CrdtId = ToDomainId(protoElement.Id),
            Value = protoElement.Value[0],
            PredecessorId = protoElement.PredecessorId != null ? ToDomainId(protoElement.PredecessorId) : null,
            SuccessorId = protoElement.SuccessorId != null ? ToDomainId(protoElement.SuccessorId) : null,
            IsDeleted = protoElement.IsDeleted
        };
    }
}
