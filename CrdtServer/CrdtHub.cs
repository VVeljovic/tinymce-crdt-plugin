using System.Text.Json;
using CrdtServer.RabbitMQ.Producer;
using CrdtServer.Services;
using Microsoft.AspNetCore.SignalR;

public class CrdtHub(CrdtDocumentStore store, IOperationProducer producer) : Hub
{
    private static readonly JsonSerializerOptions OfflineOperationJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task JoinDocument(string docId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, docId);

        var document = await store.GetOrCreate(docId);

        var resolved = document.ResolveFormatting();

        await Clients.Caller.SendAsync("ElementsChanged", document.Elements, resolved);
        await Clients.Caller.SendAsync("FormattingsChanged", document.Formattings, resolved);
    }

    public async Task Insert(CrdtCore.CrdtElement crdtElement, string docId)
    {
        var document = await store.GetOrCreate(docId);

        document.Insert(crdtElement);
        await store.Save(docId, document);

        var resolved = document.ResolveFormatting();

        await Clients.GroupExcept(docId, Context.ConnectionId).SendAsync("ElementsChanged", document.Elements, resolved);
        await Clients.Caller.SendAsync("FormattingResolved", resolved);

        await producer.SendOperation("Insert", docId, crdtElement);
    }

    public async Task Delete(CrdtCore.CrdtId crdtId, string docId)
    {
        var document = await store.GetOrCreate(docId);

        document.Delete(crdtId);
        await store.Save(docId, document);

        var resolved = document.ResolveFormatting();

        await Clients.GroupExcept(docId, Context.ConnectionId).SendAsync("ElementsChanged", document.Elements, resolved);
        await Clients.Caller.SendAsync("FormattingResolved", resolved);

        await producer.SendOperation("Delete", docId, crdtId);
    }

    public async Task ApplyFormatting(CrdtCore.CrdtFormatting formatting, string docId)
    {
        var document = await store.GetOrCreate(docId);

        document.ApplyFormatting(formatting);
        await store.Save(docId, document);

        var resolved = document.ResolveFormatting();

        await Clients.GroupExcept(docId, Context.ConnectionId).SendAsync("FormattingsChanged", document.Formattings, resolved);
        await Clients.Caller.SendAsync("FormattingResolved", resolved);

        await producer.SendOperation("Formatting", docId, formatting);
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
                        await producer.SendOperation("Insert", docId, insertElement);
                    }
                    break;
                case "Delete":
                    var deleteId = JsonSerializer.Deserialize<CrdtCore.CrdtId>(operation.Data.GetRawText(), OfflineOperationJsonOptions);
                    if (deleteId != null)
                    {
                        document.Delete(deleteId);
                        await producer.SendOperation("Delete", docId, deleteId);
                    }
                    break;
                case "Formatting":
                    var formatting = JsonSerializer.Deserialize<CrdtCore.CrdtFormatting>(operation.Data.GetRawText(), OfflineOperationJsonOptions);
                    if (formatting != null)
                    {
                        document.ApplyFormatting(formatting);
                        await producer.SendOperation("Formatting", docId, formatting);
                    }
                    break;
            }
        }

        await store.Save(docId, document);

        var resolved = document.ResolveFormatting();

        await Clients.Group(docId).SendAsync("ElementsChanged", document.Elements, resolved);
        await Clients.Group(docId).SendAsync("FormattingsChanged", document.Formattings, resolved);
    }
}
