using System.Threading.Channels;
using UavSystem.IngestionService.WebApi.Pipeline.Models;

namespace UavSystem.IngestionService.WebApi.Pipeline;

public sealed class TelemetryIngestionQueue
{
    private readonly Channel<LogPacket> _channel;
    private long _depth;
    private long _totalEnqueued;
    private long _totalDequeued;
    private long _totalRejected;

    public TelemetryIngestionQueue(int capacity)
    {
        _channel = Channel.CreateBounded<LogPacket>(new BoundedChannelOptions(capacity)
        {
            SingleReader = false,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    public bool TryEnqueue(LogPacket packet)
    {
        if (!_channel.Writer.TryWrite(packet))
        {
            Interlocked.Increment(ref _totalRejected);
            return false;
        }

        Interlocked.Increment(ref _depth);
        Interlocked.Increment(ref _totalEnqueued);
        return true;
    }

    public async IAsyncEnumerable<LogPacket> ReadAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var packet in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            Interlocked.Decrement(ref _depth);
            Interlocked.Increment(ref _totalDequeued);
            yield return packet;
        }
    }

    public TelemetryIngestionQueueSnapshot GetSnapshot()
    {
        return new TelemetryIngestionQueueSnapshot(
            Interlocked.Read(ref _depth),
            Interlocked.Read(ref _totalEnqueued),
            Interlocked.Read(ref _totalDequeued),
            Interlocked.Read(ref _totalRejected));
    }
}

public readonly record struct TelemetryIngestionQueueSnapshot(
    long Depth,
    long TotalEnqueued,
    long TotalDequeued,
    long TotalRejected);
