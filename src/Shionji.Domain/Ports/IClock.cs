namespace Shionji.Domain.Ports;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
