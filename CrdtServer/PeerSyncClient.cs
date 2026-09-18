using CrdtCore;
using Grpc.Core;
using Grpc.Net.Client;

namespace CrdtServer
{
    public class PeerSyncClient
    {
        private readonly List<AsyncDuplexStreamingCall<InsertOperationMessage, InsertOperationMessage>> _insertCalls = new();
        private readonly List<AsyncDuplexStreamingCall<DeleteOperationMessage, DeleteOperationMessage>> _deleteCalls = new();
        private readonly List<AsyncDuplexStreamingCall<FormatOperationMessage, FormatOperationMessage>> _formatCalls = new();
        private readonly ILogger<PeerSyncClient> _logger;

        private readonly SemaphoreSlim _insertLock = new(1, 1);
        private readonly SemaphoreSlim _deleteLock = new(1, 1);
        private readonly SemaphoreSlim _formatLock = new(1, 1);

        public PeerSyncClient(IConfiguration configuration, ILogger<PeerSyncClient> logger)
        {
            _logger = logger;

            var peerAddresses = (configuration["PeerAddress"] ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var address in peerAddresses)
            {
                var channel = GrpcChannel.ForAddress(address);
                var client = new CrdtService.CrdtServiceClient(channel);

                _insertCalls.Add(client.InsertOperation());
                _deleteCalls.Add(client.DeleteOperation());
                _formatCalls.Add(client.FormatOperation());

                _logger.LogInformation("Opened outbound gRPC stream to peer '{Address}'.", address);
            }
        }

        public async Task BroadcastInsertAsync(CrdtElement crdtElement, string docId)
        {
            if (_insertCalls.Count == 0)
            {
                return;
            }

            var message = new InsertOperationMessage
            {
                Element = crdtElement,
                DocId = docId,
            };

            await _insertLock.WaitAsync();
            try
            {
                foreach (var call in _insertCalls)
                {
                    try
                    {
                        await call.RequestStream.WriteAsync(message);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to forward insert to a peer.");
                    }
                }
            }
            finally
            {
                _insertLock.Release();
            }
        }

        public async Task BroadcastDeleteAsync(CrdtId elementId, string docId)
        {
            if (_deleteCalls.Count == 0)
            {
                return;
            }
            var message = new DeleteOperationMessage
            {
                ElementId = elementId,
                DocId = docId,
            };

            await _deleteLock.WaitAsync();
            try
            {
                foreach (var call in _deleteCalls)
                {
                    try
                    {
                        await call.RequestStream.WriteAsync(message);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to forward delete to a peer.");
                    }
                }
            }
            finally
            {
                _deleteLock.Release();
            }
        }

        public async Task BroadcastFormatAsync(CrdtFormatting formatting, string docId)
        {
            if (_formatCalls.Count == 0)
            {
                return;
            }

            var message = new FormatOperationMessage
            {
                Formatting = formatting,
                DocId = docId,
            };

            await _formatLock.WaitAsync();
            try
            {
                foreach (var call in _formatCalls)
                {
                    try
                    {
                        await call.RequestStream.WriteAsync(message);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to forward formatting to a peer.");
                    }
                }
            }
            finally
            {
                _formatLock.Release();
            }
        }
    }
}
