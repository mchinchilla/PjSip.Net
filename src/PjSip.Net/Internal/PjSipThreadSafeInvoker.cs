using System.Collections.Concurrent;

namespace PjSip.Net.Internal;

/// <summary>
/// Ensures PJSIP calls are dispatched on the correct thread.
/// PJSIP requires all API calls to come from the thread that registered with pjlib.
/// </summary>
internal sealed class PjSipThreadSafeInvoker : IDisposable
{
    private readonly Thread _pjThread;
    private readonly BlockingCollection<Action> _workQueue = new();
    private readonly CancellationTokenSource _cts = new();
    private volatile bool _disposed;

    public PjSipThreadSafeInvoker()
    {
        _pjThread = new Thread(ProcessWorkQueue)
        {
            Name = "PjSip-Worker",
            IsBackground = true
        };
        _pjThread.Start();
    }

    public void Invoke(Action action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _workQueue.Add(action);
    }

    public Task InvokeAsync(Action action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _workQueue.Add(() =>
        {
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    public Task<T> InvokeAsync<T>(Func<T> func)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _workQueue.Add(() =>
        {
            try
            {
                tcs.SetResult(func());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    private void ProcessWorkQueue()
    {
        try
        {
            foreach (var action in _workQueue.GetConsumingEnumerable(_cts.Token))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    // Fire-and-forget actions must not kill the worker thread.
                    // InvokeAsync actions handle exceptions via TaskCompletionSource,
                    // but Invoke (fire-and-forget) has no such handler — catch here.
                    System.Diagnostics.Debug.WriteLine($"PjSip-Worker: unhandled exception in queued action: {ex}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _workQueue.CompleteAdding();
        _pjThread.Join(TimeSpan.FromSeconds(5));
        _workQueue.Dispose();
        _cts.Dispose();
    }
}
