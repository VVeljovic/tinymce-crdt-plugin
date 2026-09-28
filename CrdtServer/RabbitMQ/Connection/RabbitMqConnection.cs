using RabbitMQ.Client;

namespace CrdtServer.RabbitMQ.Connection
{
    public class RabbitMqConnection : IRabbitMqConnection, IDisposable
    {
        private IConnection? _connection { get; set; }

        public IConnection Connection => _connection!;

        public IConfiguration _configuration;

        public RabbitMqConnection(IConfiguration configuration)
        {
            _configuration = configuration;
            _connection = InitializeConnection().GetAwaiter().GetResult();
        }

        private async Task<IConnection> InitializeConnection()
        {
            var factory = new ConnectionFactory
            {
                HostName = _configuration["RabbitMq:HostName"] ?? "localhost",
                Port = int.TryParse(_configuration["RabbitMq:Port"], out var port) ? port : 5672
            };

            return await factory.CreateConnectionAsync();
        }

        public void  Dispose()
        {
            _connection!.Dispose();
        }
    }
}
