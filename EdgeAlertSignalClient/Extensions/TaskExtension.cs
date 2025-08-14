using System;
using System.Threading;
using System.Threading.Tasks;

namespace EdgeAlertSignalClient.Extensions
{
    public static class TaskExtension
    {
        public async static void Await(this Task task, Action<Exception> errorCallback)
        {
            try
            {
                await task;
            }
            catch (Exception ex)
            {
                errorCallback?.Invoke(ex);
            }
        }
        public static async Task<T> WithCancellation<T>(this Task<T> task, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            using (cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).TrySetResult(true), tcs))
            {
                if (task != await Task.WhenAny(task, tcs.Task))
                    throw new OperationCanceledException(cancellationToken);
            }

            return await task;
        }
    }
}
