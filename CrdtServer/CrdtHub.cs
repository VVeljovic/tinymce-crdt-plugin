using CrdtServer;
using CrdtServer.Services;
using Microsoft.AspNetCore.SignalR;

public class CrdtHub(CrdtDocumentStore store, PeerSyncClient peerSyncClient) : Hub
{
    public async Task JoinDocument(string docId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, docId);

        var document = store.GetOrCreate(docId);

        await Clients.Caller.SendAsync("ElementsChanged", document.Elements);
        await Clients.Caller.SendAsync("FormattingsChanged", document.Formattings);
    }

    public async Task Insert(CrdtCore.CrdtElement crdtElement, string docId)
    {
        var document = store.GetOrCreate(docId);

        document.Insert(crdtElement);

        await Clients.GroupExcept(docId, Context.ConnectionId).SendAsync("ElementsChanged", document.Elements);

        await peerSyncClient.BroadcastInsertAsync(ToWireElement(crdtElement), docId);
    }

    public async Task Delete(CrdtCore.CrdtId crdtId, string docId)
    {
        var document = store.GetOrCreate(docId);

        document.Delete(crdtId);

        await Clients.GroupExcept(docId, Context.ConnectionId).SendAsync("ElementsChanged", document.Elements);

        await peerSyncClient.BroadcastDeleteAsync(ToWireId(crdtId), docId);
    }

    public async Task ApplyFormatting(CrdtCore.CrdtFormatting formatting, string docId)
    {
        var document = store.GetOrCreate(docId);

        document.ApplyFormatting(formatting);

        await Clients.GroupExcept(docId, Context.ConnectionId).SendAsync("FormattingsChanged", document.Formattings);

        await peerSyncClient.BroadcastFormatAsync(ToWireFormatting(formatting), docId);
    }

    private static CrdtId ToWireId(CrdtCore.CrdtId id) =>
        new CrdtId { NodeId = id.NodeId, Counter = id.Counter };

    private static CrdtElement ToWireElement(CrdtCore.CrdtElement element) =>
        new CrdtElement
        {
            Id = ToWireId(element.CrdtId),
            Value = element.Value.ToString(),
            PredecessorId = element.PredecessorId != null ? ToWireId(element.PredecessorId) : null,
            SuccessorId = element.SuccessorId != null ? ToWireId(element.SuccessorId) : null,
            IsDeleted = element.IsDeleted
        };

    private static CrdtAnchor ToWireAnchor(CrdtCore.CrdtAnchor anchor) =>
        new CrdtAnchor
        {
            Id = anchor.Id != null ? ToWireId(anchor.Id) : null,
            Type = (AnchorType)anchor.Type
        };

    private static CrdtFormatting ToWireFormatting(CrdtCore.CrdtFormatting formatting)
    {
        var wire = new CrdtFormatting
        {
            FormattingId = ToWireId(formatting.FormattingId),
            Start = ToWireAnchor(formatting.Start),
            End = ToWireAnchor(formatting.End),
        };
        wire.Attributes.Add(formatting.Attributes);

        return wire;
    }
}
