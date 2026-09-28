using System.Text.Json;
using CrdtCore;
using Microsoft.AspNetCore.SignalR;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace CrdtServer.Services
{
    public class OperationConsumer(
        IConfiguration configuration,
        CrdtDocumentStore store,
        IHubContext<CrdtHub> hubContext,
        ILogger<OperationConsumer> logger) : BackgroundService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        private readonly string _hostName = configuration["RabbitMq:HostName"] ?? "localhost";
        private readonly int _port = int.TryParse(configuration["RabbitMq:Port"], out var port) ? port : 5672;
        private readonly string _exchangeName = configuration["RabbitMq:ExchangeName"] ?? "crdt-operations";
        private readonly string _instanceId = configuration["RabbitMq:InstanceId"] ?? "default";

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "RabbitMQ consumer loop failed, retrying in 5s.");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
        }

        private async Task RunAsync(CancellationToken stoppingToken)
        {
            var factory = new ConnectionFactory
            {
                HostName = _hostName,
                Port = _port,
            };

            await using var connection = await factory.CreateConnectionAsync(stoppingToken);
            await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

            await channel.ExchangeDeclareAsync(_exchangeName, ExchangeType.Fanout, durable: true, cancellationToken: stoppingToken);

            var queueName = $"crdt-queue-{_instanceId}";
            await channel.QueueDeclareAsync(queueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
            await channel.QueueBindAsync(queueName, _exchangeName, routingKey: string.Empty, cancellationToken: stoppingToken);


            await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (_, ea) => await HandleMessageAsync(channel, ea);

            await channel.BasicConsumeAsync(queueName, autoAck: false, consumer, cancellationToken: stoppingToken);

            logger.LogInformation("Listening on queue '{Queue}' bound to exchange '{Exchange}'.", queueName, _exchangeName);

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }

        private async Task HandleMessageAsync(IChannel channel, BasicDeliverEventArgs ea)
        {
            try
            {
                var envelope = JsonSerializer.Deserialize<OperationEnvelope>(ea.Body.Span, JsonOptions);

                // Skip our own operations coming back around the fanout -
                // we already have them locally, we published them.
                if (envelope != null && envelope.Origin != _instanceId)
                {
                    await ApplyAsync(envelope);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to process a message from RabbitMQ, dropping it.");
            }
            finally
            {
                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
            }
        }

        private async Task ApplyAsync(OperationEnvelope envelope)
        {
            var document = await store.GetOrCreate(envelope.DocId);

            switch (envelope.Type)
            {
                case "Insert":
                    var element = envelope.Data.Deserialize<CrdtElement>(JsonOptions);
                    if (element != null)
                    {
                        document.Insert(element);
                        await hubContext.Clients.Group(envelope.DocId).SendAsync("ElementsChanged", document.Elements);
                    }
                    break;

                case "Delete":
                    var id = envelope.Data.Deserialize<CrdtId>(JsonOptions);
                    if (id != null)
                    {
                        document.Delete(id);
                        await hubContext.Clients.Group(envelope.DocId).SendAsync("ElementsChanged", document.Elements);
                    }
                    break;

                case "Formatting":
                    var formatting = envelope.Data.Deserialize<CrdtFormatting>(JsonOptions);
                    if (formatting != null)
                    {
                        document.ApplyFormatting(formatting);
                        await hubContext.Clients.Group(envelope.DocId).SendAsync("FormattingsChanged", document.Formattings);
                    }
                    break;
            }

            _ = store.Save(envelope.DocId, document);
        }

        private sealed record OperationEnvelope(string Type, string DocId, string Origin, JsonElement Data);
    }
}
