using Grpc.Core;
using Grpc.Net.Client;

namespace CrdtServer
{
    public class PeerSyncClient
    {
        // One open duplex stream per peer - opened once here and kept alive for the
        // lifetime of the server, not re-opened on every insert.
        private readonly List<AsyncDuplexStreamingCall<InsertElementMessage, InsertElementMessage>> _calls = new();
        private readonly ILogger<PeerSyncClient> _logger;

        public PeerSyncClient(IConfiguration configuration, ILogger<PeerSyncClient> logger)
        {
            _logger = logger;

            var peerAddresses = (configuration["PeerAddress"] ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var address in peerAddresses)
            {
                var channel = GrpcChannel.ForAddress(address);
                var client = new CrdtService.CrdtServiceClient(channel);

                _calls.Add(client.InsertElement());

                _logger.LogInformation("Opened outbound gRPC stream to peer '{Address}'.", address);
            }
        }

        public async Task BroadcastInsertAsync(string docId, char value, int visibleIndex)
        {
            if (_calls.Count == 0)
            {
                return;
            }

            var message = new InsertElementMessage
            {
                DocId = docId,
                Value = value.ToString(),
                VisibleIndex = visibleIndex
            };

            foreach (var call in _calls)
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
    }
}
