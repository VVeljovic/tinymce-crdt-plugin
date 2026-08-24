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

        var nodeId = GenerateNodeId();
        Context.Items["nodeId"] = nodeId;

        await Clients.Caller.SendAsync("ContentChanged", document.GetText());

        return nodeId;
    }

    public async Task Insert(string docId, char value, int visibleIndex)
    {
        var document = _documents[docId];
        var nodeId = (int)Context.Items["nodeId"]!;

        document.Insert(nodeId, value, visibleIndex);

        await Clients.GroupExcept(docId, Context.ConnectionId).SendAsync("ContentChanged", document.GetText());
    }

    public async Task Delete(string docId, int visibleIndex)
    {
        var document = _documents[docId];

        document.LocalDelete(visibleIndex);

        await Clients.GroupExcept(docId, Context.ConnectionId).SendAsync("ContentChanged", document.GetText());
    }

    public async Task SendFormatChange(string docId, int visibleIndex, bool isBold, bool isItalic)
    {
        var document = _documents[docId];
        var element = document.FindVisibleElementAt(visibleIndex);

        if (element == null)
        {
            return;
        }

        element.IsBold = isBold;
        element.IsItalic = isItalic;

        await Clients.Group(docId).SendAsync("ReceiveFormatChange", visibleIndex, isBold, isItalic);
    }

    public static int GenerateNodeId() => Interlocked.Increment(ref _nextNodeId);
}