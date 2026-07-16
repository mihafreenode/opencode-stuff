using System.Reflection;

namespace OpenCode.Workspace.Mcp.Tests;

internal static class TransportDisposal
{
    public static async Task TryDisposeAsync(object? transport)
    {
        if (transport is null)
        {
            return;
        }

        var transportType = transport.GetType();
        var disposeAsync = transportType.GetMethod("DisposeAsync", BindingFlags.Instance | BindingFlags.Public, []);
        if (disposeAsync is not null)
        {
            if (disposeAsync.Invoke(transport, null) is ValueTask valueTask)
            {
                await valueTask;
                return;
            }

            if (disposeAsync.Invoke(transport, null) is Task task)
            {
                await task;
                return;
            }
        }

        var dispose = transportType.GetMethod("Dispose", BindingFlags.Instance | BindingFlags.Public, []);
        dispose?.Invoke(transport, null);
    }
}
