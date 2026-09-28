namespace CrdtServer.RabbitMQ.Producer
{
    public interface IOperationProducer
    {
        Task SendOperation<T>(string type, string docId, T data);
    }
}
