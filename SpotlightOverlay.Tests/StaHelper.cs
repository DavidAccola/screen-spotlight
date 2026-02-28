using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Windows.Threading;

namespace SpotlightOverlay.Tests;

/// <summary>
/// Helper to run WPF-dependent test code on a single shared STA thread,
/// avoiding Win32 Dispatcher resource exhaustion. All WPF tests share one
/// STA thread to prevent creating multiple Dispatchers that exhaust handles.
/// </summary>
internal static class StaHelper
{
    private static readonly object _lock = new();
    private static Thread? _staThread;
    private static BlockingCollection<(Action work, ManualResetEventSlim done, Exception?[] error)>? _queue;
    private static bool? _canRunWpf;

    /// <summary>
    /// Returns true if WPF rendering types can be initialized in this environment.
    /// Caches the result after the first check.
    /// </summary>
    public static bool CanRunWpf
    {
        get
        {
            if (_canRunWpf.HasValue)
                return _canRunWpf.Value;

            try
            {
                Run(() =>
                {
                    // Try to create a DrawingGroup to verify WPF rendering works
                    _ = new System.Windows.Media.DrawingGroup();
                });
                _canRunWpf = true;
            }
            catch
            {
                _canRunWpf = false;
            }

            return _canRunWpf.Value;
        }
    }

    private static void EnsureThread()
    {
        lock (_lock)
        {
            if (_staThread != null && _staThread.IsAlive)
                return;

            _queue = new BlockingCollection<(Action, ManualResetEventSlim, Exception?[])>();
            _staThread = new Thread(() =>
            {
                // Ensure the WPF Dispatcher is created on this STA thread
                _ = Dispatcher.CurrentDispatcher;

                foreach (var (work, done, error) in _queue.GetConsumingEnumerable())
                {
                    try
                    {
                        work();
                    }
                    catch (Exception ex)
                    {
                        error[0] = ex;
                    }
                    finally
                    {
                        done.Set();
                    }
                }
            });
            _staThread.SetApartmentState(ApartmentState.STA);
            _staThread.IsBackground = true;
            _staThread.Start();
        }
    }

    public static void Run(Action action)
    {
        EnsureThread();

        var error = new Exception?[1];
        using var done = new ManualResetEventSlim(false);
        _queue!.Add((action, done, error));
        done.Wait();

        if (error[0] != null)
            throw error[0]!;
    }
}
