using System.Windows.Input;

namespace Q.Sock.UI.ViewModels;

public sealed class AsyncCommand(Func<Task> execute) : ICommand
{
    private bool running;
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !running;

    public async void Execute(object? parameter) => await ExecuteAsync();

    public async Task ExecuteAsync()
    {
        if (running)
            return;
        running = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            await execute();
        }
        finally
        {
            running = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
