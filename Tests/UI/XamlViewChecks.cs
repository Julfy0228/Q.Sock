using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Q.Sock.Core;
using Q.Sock.UI;
using Q.Sock.UI.ViewModels;
using Q.Sock.UI.Views;

namespace Q.Sock.Tests.UI;

internal static class XamlViewChecks
{
    public static void Run()
    {
        CheckLoginBindings();
        CheckSettingsBindings();
        CheckAppearanceBindings();
        CheckWorkspaceLayout();
        Console.WriteLine(
            "PASS: XAML login bindings and Enter, saved accounts, settings commands and rotation, theme editing and live resources, adaptive workspace."
        );
    }

    private static void CheckLoginBindings()
    {
        var loginCount = 0;
        var savedCount = 0;
        var received = "";
        var model = new LoginViewModel(login: input =>
        {
            loginCount++;
            received = input.UserName + ":" + input.Password;
            return Task.CompletedTask;
        })
        {
            ServerUrl = "https://example.test",
            AllowRegistration = false,
        };
        model.SavedAccounts.Add(
            new(
                "Аккаунт проверки",
                new(() =>
                {
                    savedCount++;
                    return Task.CompletedTask;
                })
            )
        );
        var view = new LoginView { DataContext = model };
        WithWindow(
            view,
            window =>
            {
                var username = view.FindControl<TextBox>("UserNameInput")!;
                var password = view.FindControl<TextBox>("PasswordInput")!;
                username.Text = "BindingCheck";
                password.Text = "test-password";
                Dispatcher.UIThread.RunJobs();
                password.RaiseEvent(
                    new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }
                );
                Check(
                    loginCount == 1 && received == "BindingCheck:test-password",
                    "Вход по Enter потерял значения полей."
                );
                var register = Button(view, "Создать аккаунт");
                Check(!register.IsVisible, "Регистрация должна зависеть от политики сервера.");
                model.AllowRegistration = true;
                Dispatcher.UIThread.RunJobs();
                Check(register.IsVisible, "Изменение политики не обновило XAML.");
                Button(view, "Аккаунт проверки").Command!.Execute(null);
                Check(savedCount == 1, "Команда сохранённого аккаунта не привязалась.");
                var custom = new MessengerTheme();
                custom.Colors["accent"] = "#A12345";
                MessengerStyles.ApplyPalette((Border)window.Content!, custom);
                Dispatcher.UIThread.RunJobs();
                Check(
                    ((ISolidColorBrush)Button(view, "Войти").Background!).Color
                        == Color.Parse("#A12345"),
                    "XAML должен сохранять пользовательскую палитру."
                );
            }
        );
        var browser = new LoginView { DataContext = new LoginViewModel { ShowServer = false } };
        WithWindow(
            browser,
            _ =>
                Check(
                    !browser.FindControl<TextBox>("ServerUrlInput")!.IsEffectivelyVisible,
                    "В браузере не должно быть выбора сервера."
                )
        );
    }

    private static void CheckSettingsBindings()
    {
        var removed = 0;
        var revoked = 0;
        var rotationCount = 0;
        var model = new SettingsViewModel { ShowRotation = true, AllowRotation = true };
        model.RotationChanged += (_, _) => rotationCount++;
        var view = new SettingsView { DataContext = model };
        WithWindow(
            view,
            _ =>
            {
                model.Accounts.Add(
                    new(
                        "Сохранённый аккаунт",
                        new(() => Task.CompletedTask),
                        new(() =>
                        {
                            removed++;
                            return Task.CompletedTask;
                        })
                    )
                );
                model.Sessions.Add(
                    new(
                        "Тестовое устройство",
                        "Это устройство",
                        new(() =>
                        {
                            revoked++;
                            return Task.CompletedTask;
                        })
                    )
                );
                Dispatcher.UIThread.RunJobs();
                view.UpdateLayout();
                Button(view, "Убрать из сохранённых").Command!.Execute(null);
                Button(view, "Завершить сессию").Command!.Execute(null);
                var rotation = view.GetVisualDescendants().OfType<CheckBox>().Single();
                rotation.IsChecked = false;
                Dispatcher.UIThread.RunJobs();
                Check(
                    removed == 1 && revoked == 1,
                    "Динамические команды аккаунтов или сессий потеряны."
                );
                Check(
                    !model.AllowRotation && rotationCount == 1,
                    "Переключатель поворота не обновляет модель."
                );
                Check(
                    !Button(view, "Сканировать QR-код камерой").IsVisible,
                    "Камера должна зависеть от платформы."
                );
            }
        );
    }

    private static void CheckAppearanceBindings()
    {
        MessengerTheme? applied = null;
        var reset = false;
        var model = new AppearanceViewModel(
            new MessengerTheme(),
            theme =>
            {
                applied = MessengerTheme.Parse(theme.Export());
                return Task.CompletedTask;
            },
            () =>
            {
                reset = true;
                return Task.CompletedTask;
            }
        );
        var view = new AppearanceView { DataContext = model };
        WithWindow(
            view,
            _ =>
            {
                var inputs = view.GetVisualDescendants().OfType<TextBox>().ToArray();
                Check(inputs.Length == 7, "Редактор должен содержать название и шесть цветов.");
                inputs[0].Text = "Проверка привязки";
                inputs[1].Text = "#123456";
                Dispatcher.UIThread.RunJobs();
                Button(view, "Применить").Command!.Execute(null);
                Check(
                    applied?.Name == "Проверка привязки"
                        && applied.Colors["background"] == "#123456",
                    "Редактор отправил старые значения темы."
                );
                Button(view, "Сбросить тему").Command!.Execute(null);
                Check(reset, "Команда сброса темы не подключена.");
            }
        );
    }

    private static void CheckWorkspaceLayout()
    {
        var view = new WorkspaceView();
        var sidebar = view.FindControl<Border>("SidebarPanel")!;
        var members = view.FindControl<Border>("MembersPanel")!;
        WithWindow(
            view,
            window =>
            {
                void Arrange(double width, bool showingConversation, bool directChat, bool? membersOpen = null)
                {
                    window.Width = width;
                    view.SetLayout(width, showingConversation, directChat, membersOpen);
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                }

                Arrange(390, false, false);
                Check(
                    sidebar.IsEffectivelyVisible && sidebar.Bounds.Width == view.Workspace.Bounds.Width,
                    "Список чатов должен занимать всю ширину узкого окна."
                );
                Arrange(390, true, false);
                Check(
                    !sidebar.IsVisible
                        && view.Conversation.IsVisible
                        && view.Conversation.Bounds.Width == view.Workspace.Bounds.Width,
                    "В узком окне переписка должна заменять список."
                );
                Arrange(1280, true, false);
                Check(
                    members.IsVisible
                        && sidebar.Bounds.Right == view.Conversation.Bounds.Left
                        && view.Conversation.Bounds.Right == members.Bounds.Left
                        && members.Bounds.Right == view.Workspace.Bounds.Width,
                    "В широком окне чат должен быть между каналами и участниками."
                );
                Arrange(1280, true, true);
                Check(
                    !members.IsVisible
                        && view.Conversation.Bounds.Right == view.Workspace.Bounds.Width,
                    "В личном чате не должно оставаться места под панель участников."
                );
                Arrange(760, false, false);
                Check(
                    sidebar.IsEffectivelyVisible && !view.FindControl<Border>("BottomBar")!.IsVisible,
                    "Граница адаптивности изменилась."
                );
                Arrange(390, true, false, true);
                Check(
                    members.IsEffectivelyVisible && !view.Conversation.IsVisible
                        && members.Bounds.Width == view.Workspace.Bounds.Width,
                    "На телефоне список участников должен заменять переписку."
                );
                Arrange(1280, true, false, false);
                Check(
                    !members.IsVisible && view.Conversation.Bounds.Right == view.Workspace.Bounds.Width,
                    "Закрытая панель не должна занимать место."
                );
            }
        );
    }

    private static Button Button(Control view, string label) =>
        view.GetVisualDescendants()
            .OfType<Button>()
            .Single(button => Equals(button.Content, label));

    private static void WithWindow(Control content, Action<Window> action)
    {
        var host = new Border { Child = content };
        host.Styles.Add(new MessengerStyles());
        MessengerStyles.ApplyPalette(host, new MessengerTheme());
        var window = new Window
        {
            Width = 560,
            Height = 1000,
            Content = host,
        };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            action(window);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
