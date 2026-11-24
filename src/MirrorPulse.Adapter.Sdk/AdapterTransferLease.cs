namespace MirrorPulse.Adapter.Sdk;

/// <summary>Owns a temporary transfer file that is deleted when its stream closes.</summary>
/// <remarks>Create the lease inside the transfer-cache directory granted by the Host. Dispose it on
/// success, failure and session shutdown. After a cancellation, await cleanup before sending the acknowledgement.</remarks>
public sealed class AdapterTransferLease : IAsyncDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private bool _disposed;

    public AdapterTransferLease(string transferCacheDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transferCacheDirectory);
        Directory.CreateDirectory(transferCacheDirectory);
        Path = System.IO.Path.Combine(transferCacheDirectory, Guid.NewGuid().ToString("N") + ".transfer");
        Stream = new FileStream(Path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
            64 * 1024, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
    }

    public string Path { get; }
    /// <summary>The exclusive, readable and writable stream for the staged transfer.</summary>
    public FileStream Stream { get; }

    /// <summary>The token signaled when <see cref="CancelAsync"/> is called.</summary>
    public CancellationToken CancellationToken => _cancellation.Token;

    /// <summary>Signals receiving work and disposes the staged transfer.</summary>
    /// <remarks>An accepted backend mutation has its own recovery contract.</remarks>
    public async ValueTask CancelAsync()
    {
        if (_disposed) return;
        await _cancellation.CancelAsync().ConfigureAwait(false);
        await DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await Stream.DisposeAsync().ConfigureAwait(false);
        _cancellation.Dispose();
    }
}
