using System.Text.Json;
using CrdtServer;
using CrdtServer.Services;
using Microsoft.AspNetCore.SignalR;

public class CrdtHub(CrdtDocumentStore store, PeerSyncClient peerSyncClient) : Hub
{
    private static readonly JsonSerializerOptions OfflineOperationJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task JoinDocument(string docId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, docId);

        var document = await store.GetOrCreate(docId);

        await Clients.Caller.SendAsync("ElementsChanged", document.Elements);
        await Clients.Caller.SendAsync("FormattingsChanged", document.Formattings);
    }

    public async Task Insert(CrdtCore.CrdtElement crdtElement, string docId)
    {
        var document = await store.GetOrCreate(docId);

        document.Insert(crdtElement);
        store.MarkDirty(docId);

        await Clients.GroupExcept(docId, Context.ConnectionId).SendAsync("ElementsChanged", document.Elements);

        await peerSyncClient.BroadcastInsertAsync(ToWireElement(crdtElement), docId);
    }

    public async Task Delete(CrdtCore.CrdtId crdtId, string docId)
    {
        var document = await store.GetOrCreate(docId);

        document.Delete(crdtId);
        store.MarkDirty(docId);

        await Clients.GroupExcept(docId, Context.ConnectionId).SendAsync("ElementsChanged", document.Elements);

        await peerSyncClient.BroadcastDeleteAsync(ToWireId(crdtId), docId);
    }

    public async Task ApplyFormatting(CrdtCore.CrdtFormatting formatting, string docId)
    {
        var document = await store.GetOrCreate(docId);

        document.ApplyFormatting(formatting);
        store.MarkDirty(docId);

        await Clients.GroupExcept(docId, Context.ConnectionId).SendAsync("FormattingsChanged", document.Formattings);

        await peerSyncClient.BroadcastFormatAsync(ToWireFormatting(formatting), docId);
    }

    public async Task ApplyOfflineOperations(List<CrdtCore.OfflineOperations> operations, string docId)
    {
        var document = await store.GetOrCreate(docId);

        foreach (var operation in operations)
        {
            switch (operation.Type)
            {
                case "Insert":
                    var insertElement = JsonSerializer.Deserialize<CrdtCore.CrdtElement>(operation.Data.GetRawText(), OfflineOperationJsonOptions);
                    if (insertElement != null)
                    {
                        document.Insert(insertElement);
                        await peerSyncClient.BroadcastInsertAsync(ToWireElement(insertElement), docId);
                    }
                    break;
                case "Delete":
                    var deleteId = JsonSerializer.Deserialize<CrdtCore.CrdtId>(operation.Data.GetRawText(), OfflineOperationJsonOptions);
                    if (deleteId != null)
                    {
                        document.Delete(deleteId);
                        await peerSyncClient.BroadcastDeleteAsync(ToWireId(deleteId), docId);
                    }
                    break;
                case "Formatting":
                    var formatting = JsonSerializer.Deserialize<CrdtCore.CrdtFormatting>(operation.Data.GetRawText(), OfflineOperationJsonOptions);
                    if (formatting != null)
                    {
                        document.ApplyFormatting(formatting);
                        await peerSyncClient.BroadcastFormatAsync(ToWireFormatting(formatting), docId);
                    }
                    break;
            }
        }

        store.MarkDirty(docId);

        await Clients.Group(docId).SendAsync("ElementsChanged", document.Elements);
        await Clients.Group(docId).SendAsync("FormattingsChanged", document.Formattings);
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
