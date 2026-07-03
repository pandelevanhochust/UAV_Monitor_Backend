using System.Threading.Channels;
using UavSystem.IngestionService.WebApi.Pipeline.Models;

namespace UavSystem.IngestionService.WebApi.Pipeline;

public sealed class TelemetryIngestionQueue
{
    private readonly Channel<LogPacket> _channel;

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
        return _channel.Writer.TryWrite(packet);
    }

    public IAsyncEnumerable<LogPacket> ReadAllAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
