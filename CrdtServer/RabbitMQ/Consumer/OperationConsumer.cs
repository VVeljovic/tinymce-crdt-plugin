using CrdtServer.RabbitMQ.Connection;
using CrdtServer.Services;
using Microsoft.AspNetCore.SignalR;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text.Json;

namespace CrdtServer.RabbitMQ.Consumer
{
    public class OperationConsumer(IRabbitMqConnection connection,
        IConfiguration configuration,
        CrdtDocumentStore store,
        IHubContext<CrdtHub> hubContext) : BackgroundService
    {

        private readonly string _instanceId = configuration["RabbitMq:InstanceId"]!;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var channel = await connection.Connection.CreateChannelAsync(cancellationToken: stoppingToken);

            await channel.ExchangeDeclareAsync("crdt-operations", ExchangeType.Fanout, durable: true);

            var queue = $"crdt-queue-{_instanceId}";

            await channel.QueueDeclareAsync(queue: queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
            await channel.QueueBindAsync(queue, exchange: "crdt-operations", routingKey: "", cancellationToken: stoppingToken);

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (_, ea) => await HandleMessageAsync(channel, ea);

            await channel.BasicConsumeAsync(queue, autoAck: false, consumer, cancellationToken: stoppingToken);

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        private async Task HandleMessageAsync(IChannel channel, BasicDeliverEventArgs ea)
        {
            var envelope = JsonSerializer.Deserialize<OperationEnvelope>(ea.Body.Span, JsonOptions);

            if(envelope != null && envelope.Origin != _instanceId)
            {
                await ApplyAsync(envelope);
            }

            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
        }

        private async Task ApplyAsync(OperationEnvelope envelope)
        {
            var document = await store.GetOrCreate(envelope.DocId);

            switch (envelope.Type)
            {
                case "Insert":
                    var element = envelope.Data.Deserialize<CrdtCore.CrdtElement>(JsonOptions);
                    if (element != null)
                    {
                        document.Insert(element);
                        await hubContext.Clients.Group(envelope.DocId).SendAsync("ElementsChanged", document.Elements);
                    }
                    break;

                case "Delete":
                    var id = envelope.Data.Deserialize<CrdtCore.CrdtId>(JsonOptions);
                    if (id != null)
                    {
                        document.Delete(id);
                        await hubContext.Clients.Group(envelope.DocId).SendAsync("ElementsChanged", document.Elements);
                    }
                    break;

                case "Formatting":
                    var formatting = envelope.Data.Deserialize<CrdtCore.CrdtFormatting>(JsonOptions);
                    if (formatting != null)
                    {
                        document.ApplyFormatting(formatting);
                        await hubContext.Clients.Group(envelope.DocId).SendAsync("FormattingsChanged", document.Formattings);
                    }
                    break;
            }

            await store.Save(envelope.DocId, document);
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        private sealed record OperationEnvelope(string Type, string DocId, string Origin, JsonElement Data);
    }
}