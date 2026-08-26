using CrdtCore;
using CrdtServer;
using CrdtServer.Services;
using Microsoft.AspNetCore.SignalR;

public class CrdtHub(CrdtDocumentStore store, PeerSyncClient peerSyncClient) : Hub
{
    private static int _nextNodeId = 1;

    public async Task<int> JoinDocument(string docId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, docId);

        var document = store.GetOrCreate(docId);

        var nodeId = GenerateNodeId();
        Context.Items["nodeId"] = nodeId;

        await Clients.Caller.SendAsync("ContentChanged", document.GetText());

        return nodeId;
    }

    public async Task Insert(string docId, char value, int visibleIndex)
    {
        var document = store.GetOrCreate(docId);
        var nodeId = (int)Context.Items["nodeId"]!;

        document.Insert(nodeId, value, visibleIndex);

        await Clients.GroupExcept(docId, Context.ConnectionId).SendAsync("ContentChanged", document.GetText());

        await peerSyncClient.BroadcastInsertAsync(docId, value, visibleIndex);
    }

    public async Task Delete(string docId, int visibleIndex)
    {
        var document = store.GetOrCreate(docId);

        document.LocalDelete(visibleIndex);

        await Clients.GroupExcept(docId, Context.ConnectionId).SendAsync("ContentChanged", document.GetText());
    }

    public static int GenerateNodeId() => Interlocked.Increment(ref _nextNodeId);
}
