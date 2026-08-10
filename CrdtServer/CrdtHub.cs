using CrdtCore;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

public class CrdtHub : Hub
{
    private static readonly ConcurrentDictionary<string, CrdtDocument> _documents = new();

    private static int _nextNodeId = 1;

    public async Task<int> JoinDocument(string docId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, docId);

        var document = _documents.GetOrAdd(docId, _ => new CrdtDocument());

        await Clients.Caller.SendAsync("FullSync", document.Elements);

        return GenerateNodeId();
    }

    public async Task SendInsert(string docId, CrdtElement element)
    {
        var document = _documents[docId];

        document.RemoteInsert(element);

        await Clients.OthersInGroup(docId).SendAsync("ReceiveInsert", element);
    }

    public async Task SendDelete(string docId, CrdtId elementId)
    {
        var document = _documents[docId];
        document.RemoteDelete(elementId);

        await Clients.OthersInGroup(docId).SendAsync("ReceiveDelete", elementId);
    }

    public static int GenerateNodeId() => Interlocked.Increment(ref _nextNodeId);
}