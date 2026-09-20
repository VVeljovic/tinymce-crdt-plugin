using System.Text.Json;
using CrdtServer.Services;
using Microsoft.AspNetCore.SignalR;

public class CrdtHub(CrdtDocumentStore store, OperationPublisher publisher) : Hub
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
        _ = store.Save(docId, document);

        await Clients.GroupExcept(docId, Context.ConnectionId).SendAsync("ElementsChanged", document.Elements);

        _ = publisher.PublishInsertAsync(crdtElement, docId);
    }

    public async Task Delete(CrdtCore.CrdtId crdtId, string docId)
    {
        var document = await store.GetOrCreate(docId);

        document.Delete(crdtId);
        _ = store.Save(docId, document);

        await Clients.GroupExcept(docId, Context.ConnectionId).SendAsync("ElementsChanged", document.Elements);

        _ = publisher.PublishDeleteAsync(crdtId, docId);
    }

    public async Task ApplyFormatting(CrdtCore.CrdtFormatting formatting, string docId)
    {
        var document = await store.GetOrCreate(docId);

        document.ApplyFormatting(formatting);
        _ = store.Save(docId, document);

        await Clients.GroupExcept(docId, Context.ConnectionId).SendAsync("FormattingsChanged", document.Formattings);

        _ = publisher.PublishFormatAsync(formatting, docId);
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
                        _ = publisher.PublishInsertAsync(insertElement, docId);
                    }
                    break;
                case "Delete":
                    var deleteId = JsonSerializer.Deserialize<CrdtCore.CrdtId>(operation.Data.GetRawText(), OfflineOperationJsonOptions);
                    if (deleteId != null)
                    {
                        document.Delete(deleteId);
                        _ = publisher.PublishDeleteAsync(deleteId, docId);
                    }
                    break;
                case "Formatting":
                    var formatting = JsonSerializer.Deserialize<CrdtCore.CrdtFormatting>(operation.Data.GetRawText(), OfflineOperationJsonOptions);
                    if (formatting != null)
                    {
                        document.ApplyFormatting(formatting);
                        _ = publisher.PublishFormatAsync(formatting, docId);
                    }
                    break;
            }
        }

        _ = store.Save(docId, document);

        await Clients.Group(docId).SendAsync("ElementsChanged", document.Elements);
        await Clients.Group(docId).SendAsync("FormattingsChanged", document.Formattings);
    }
}
