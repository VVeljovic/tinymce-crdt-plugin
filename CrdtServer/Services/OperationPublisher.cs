using System.Text.Json;
using CrdtCore;
using RabbitMQ.Client;

namespace CrdtServer.Services
{
    public class OperationPublisher : IAsyncDisposable
    {
        private readonly ILogger<OperationPublisher> _logger;
        private readonly string _hostName;
        private readonly int _port;
        private readonly string _exchangeName;
        private readonly string _instanceId;

        private readonly SemaphoreSlim _lock = new(1, 1);

        private IConnection? _connection;
        private IChannel? _channel;

        public string InstanceId => _instanceId;

        public OperationPublisher(IConfiguration configuration, ILogger<OperationPublisher> logger)
        {
            _logger = logger;
            _hostName = configuration["RabbitMq:HostName"] ?? "localhost";
            _port = int.TryParse(configuration["RabbitMq:Port"], out var port) ? port : 5672;
            _exchangeName = configuration["RabbitMq:ExchangeName"] ?? "crdt-operations";
            _instanceId = configuration["RabbitMq:InstanceId"] ?? "default";
        }

        public Task PublishInsertAsync(CrdtElement element, string docId) =>
            PublishAsync("Insert", element, docId);

        public Task PublishDeleteAsync(CrdtId elementId, string docId) =>
            PublishAsync("Delete", elementId, docId);

        public Task PublishFormatAsync(CrdtFormatting formatting, string docId) =>
            PublishAsync("Formatting", formatting, docId);

        private async Task PublishAsync(string type, object data, string docId)
        {
            await _lock.WaitAsync();
            try
            {
                var channel = await GetChannelAsync();

                var envelope = new
                {
                    Type = type,
                    DocId = docId,
                    Origin = _instanceId,
                    Data = data,
                };

                var body = JsonSerializer.SerializeToUtf8Bytes(envelope);

                var properties = new BasicProperties
                {
                    Persistent = true,
                };

                await channel.BasicPublishAsync(
                    exchange: _exchangeName,
                    routingKey: string.Empty,
                    mandatory: false,
                    basicProperties: properties,
                    body: body);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish {Type} operation for doc '{DocId}' to RabbitMQ.", type, docId);
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task<IChannel> GetChannelAsync()
        {
            if (_channel != null)
            {
                return _channel;
            }

            var factory = new ConnectionFactory
            {
                HostName = _hostName,
                Port = _port,
            };

            _connection = await factory.CreateConnectionAsync();
            _channel = await _connection.CreateChannelAsync();

            await _channel.ExchangeDeclareAsync(_exchangeName, ExchangeType.Fanout, durable: true);

            _logger.LogInformation(
                "Connected to RabbitMQ at {HostName}:{Port}, exchange '{Exchange}'.",
                _hostName, _port, _exchangeName);

            return _channel;
        }

        public async ValueTask DisposeAsync()
        {
            if (_channel != null)
            {
                await _channel.CloseAsync();
                _channel.Dispose();
            }

            if (_connection != null)
            {
                await _connection.CloseAsync();
                _connection.Dispose();
            }
        }
    }
}
