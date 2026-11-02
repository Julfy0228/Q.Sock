namespace Q.Sock.UI;

public sealed partial class MessengerView
{
    private DocumentationView? documentation;

    private async Task ShowDocumentation()
    {
        var moduleIds =
            manifest?.Modules.Select(module => module.Id).Where(clientPlugins.ContainsKey) ?? [];
        var view = new DocumentationView(preferences, moduleIds)
        {
            Height = Math.Max(260, Bounds.Height - 210),
        };
        await ShowDialog("Справка Q.Sock", view, 960);
        documentation = view;
        dialogClosed = () =>
        {
            view.Dispose();
            documentation = null;
        };
        await view.Initialize();
    }
}
