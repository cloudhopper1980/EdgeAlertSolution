using System;
using System.Threading.Tasks;
using System.Windows;

public static class DispatcherHelper
{
    public static void RunOnUIThread(Action action)
    {
        if (Application.Current.Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            Application.Current.Dispatcher.Invoke(action);
        }
    }

    public static async Task RunOnUIThreadAsync(Func<Task> asyncAction)
    {
        if (Application.Current.Dispatcher.CheckAccess())
        {
            await asyncAction();
        }
        else
        {
            await Application.Current.Dispatcher.InvokeAsync(asyncAction);
        }
    }    
}
