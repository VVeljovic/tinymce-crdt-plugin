using CrdtServer;
using CrdtServer.Services;
using Microsoft.AspNetCore.SignalR;

public class CrdtHub(CrdtDocumentStore store, PeerSyncClient peerSyncClient) : Hub
{
    private static int _nextNodeId = 1;

    public async Task JoinDocument(string docId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, docId);

        var document = store.GetOrCreate(docId);

        await Clients.Caller.SendAsync("ElementsChanged", document.Elements);
    }

    public async Task Insert(CrdtCore.CrdtElement crdtElement, string docId)
    {
        var document = store.GetOrCreate(docId);

        document.RemoteInsert(crdtElement);

        await Clients.GroupExcept(docId, Context.ConnectionId).SendAsync("ElementsChanged", document.Elements);

        await peerSyncClient.BroadcastInsertAsync(ToWireElement(crdtElement), docId);
    }

    public async Task Delete(CrdtCore.CrdtId crdtId, string docId)
    {
        var document = store.GetOrCreate(docId);

        document.RemoteDelete(crdtId);

        await Clients.GroupExcept(docId, Context.ConnectionId).SendAsync("ElementsChanged", document.Elements);

        await peerSyncClient.BroadcastDeleteAsync(ToWireId(crdtId), docId);
    }

    private static CrdtId ToWireId(CrdtCore.CrdtId id) =>
        new CrdtId { NodeId = id.NodeId, Counter = id.Counter };

    private static CrdtElement ToWireElement(CrdtCore.CrdtElement element) =>
        new CrdtElement
        {
            Id = ToWireId(element.CrdtId),
            Value = element.Value.ToString(),
            PredecessorId = element.PredecessorId != null ? ToWireId(element.PredecessorId) : null,
            IsDeleted = element.IsDeleted
        };
}
