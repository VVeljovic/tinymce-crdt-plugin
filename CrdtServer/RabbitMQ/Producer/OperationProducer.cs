using CrdtServer.RabbitMQ.Connection;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;

namespace CrdtServer.RabbitMQ.Producer
{
    public class OperationProducer(IRabbitMqConnection connection, IConfiguration configuration) : IOperationProducer
    {
        public async Task SendOperation<T>(string type, string docId, T data)
        {
            using var channel = await connection.Connection.CreateChannelAsync();

            await channel.ExchangeDeclareAsync("crdt-operations", ExchangeType.Fanout, durable: true);

            var envelope = new { Type = type, DocId = docId, Origin = configuration["RabbitMq:InstanceId"], Data = data };

            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope));

            await channel.BasicPublishAsync(exchange: "crdt-operations", routingKey: "", body: body);
        }
    }
}
