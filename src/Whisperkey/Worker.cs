using System.Collections.Concurrent;

namespace Whisperkey;

/// A single serial worker thread.
///
/// The keyboard hook must return in microseconds - Windows silently drops a
/// low-level hook that exceeds LowLevelHooksTimeout, which would disable the
/// hotkey system-wide. So the hook only enqueues; everything real happens here.
/// One thread, so dictation actions cannot overlap or race each other.
sealed class Worker : IDisposable {
    readonly BlockingCollection<Action> _queue = new(new ConcurrentQueue<Action>());
    readonly Thread _thread;

    public Worker() {
        _thread = new Thread(Run) { IsBackground = true, Name = "Whisperkey.Worker" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public void Post(Action work) {
        if (!_queue.IsAddingCompleted) _queue.Add(work);
    }

    void Run() {
        foreach (var work in _queue.GetConsumingEnumerable()) {
            try { work(); }
            catch (Exception e) { Log.Write($"worker error: {e.Message}"); }
        }
    }

    public void Dispose() => _queue.CompleteAdding();
}
