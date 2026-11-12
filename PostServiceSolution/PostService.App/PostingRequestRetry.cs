using System.Net;
using System.Net.Sockets;

namespace PostService.App;

internal static class PostingRequestRetry
{
    public static async Task<bool> RunAsync(Form owner, Func<Task> request, bool repeatable = true)
    {
        while (!owner.IsDisposed)
        {
            try
            {
                await request();
                return true;
            }
            catch (HttpRequestException ex) when (CanRetry(ex, repeatable))
            {
                if (!AskToRetry(owner, "Сервер PostService недоступний. Повторити операцію?"))
                {
                    return false;
                }
            }
            catch (OperationCanceledException ex) when (repeatable && ex.InnerException is TimeoutException)
            {
                if (!AskToRetry(owner, "Сервер не відповів вчасно. Повторити операцію?"))
                {
                    return false;
                }
            }
        }

        return false;
    }

    private static bool CanRetry(HttpRequestException error, bool repeatable) =>
        error.StatusCode == HttpStatusCode.ServiceUnavailable && repeatable ||
        error.StatusCode is null && (repeatable ||
            error.HttpRequestError == HttpRequestError.NameResolutionError ||
            error.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionRefused });

    private static bool AskToRetry(Form owner, string message) =>
        !owner.IsDisposed &&
        MessageBox.Show(owner, message, "З’єднання з PostService", MessageBoxButtons.RetryCancel,
            MessageBoxIcon.Warning) == DialogResult.Retry;
}
