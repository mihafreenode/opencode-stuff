namespace OpenCode.Workspace.RemoteBridge;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var app = RemoteBridgeApplication.Build(args);
        var options = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<RemoteBridgeOptions>>().Value;
        if (!options.RemoteAccess.Enabled)
        {
            app.Logger.LogInformation("RemoteBridge is disabled. Set RemoteAccess:Enabled=true to start its loopback listener.");
            return;
        }

        await app.RunAsync();
    }
}
