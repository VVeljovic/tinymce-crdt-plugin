using System.Text.Json;

namespace CrdtCore
{
    public sealed record OfflineOperations(string Type, JsonElement Data);
}
