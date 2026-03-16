using System.Threading.Channels;

namespace Symphony.Application.Polling;

public sealed class PollingRefreshTrigger(TimeProvider timeProvider)
{
    private static readonly string[] RefreshOperations = ["poll", "reconcile"];

    private readonly Lock _stateLock = new();
    private bool _pending;
    private readonly Channel<bool> _requests = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite
        });

    public PollingRefreshReceipt RequestRefresh()
    {
        var requestedAt = timeProvider.GetUtcNow();
        bool coalesced;
        bool shouldSignal;

        lock (_stateLock)
        {
            coalesced = _pending;
            shouldSignal = !_pending;
            _pending = true;
        }

        if (shouldSignal)
        {
            _requests.Writer.TryWrite(true);
        }

        return new PollingRefreshReceipt(
            Queued: true,
            Coalesced: coalesced,
            RequestedAt: requestedAt,
            Operations: RefreshOperations);
    }

    public async Task WaitForRefreshAsync(CancellationToken cancellationToken)
    {
        await _requests.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);

        lock (_stateLock)
        {
            _pending = false;
        }
    }
}

public sealed record PollingRefreshReceipt(
    bool Queued,
    bool Coalesced,
    DateTimeOffset RequestedAt,
    IReadOnlyList<string> Operations);
