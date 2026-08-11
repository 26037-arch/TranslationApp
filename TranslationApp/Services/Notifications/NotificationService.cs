using System.Windows;
using TranslationApp.Views;

namespace TranslationApp.Services.Notifications;

public sealed class NotificationService
{
    public void Show(string message, bool isError = false)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var toast = new ToastWindow(message, isError);
            toast.Show();
        });
    }
}
