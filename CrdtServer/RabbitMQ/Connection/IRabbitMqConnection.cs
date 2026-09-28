using RabbitMQ.Client;

namespace CrdtServer.RabbitMQ.Connection
{
    public interface IRabbitMqConnection
    {
        public IConnection Connection { get; }
    }
}
