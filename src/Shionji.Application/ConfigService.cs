using Shionji.Domain.Configuration;
using Shionji.Domain.Ports;
using Shionji.Domain.ValueObjects;

namespace Shionji.Application;

/// <summary>
/// 転送設定の CRUD と永続化。接続中の設定を保存 / 削除する場合は切断してから行う。
/// </summary>
public sealed class ConfigService(
    IForwardingConfigRepository repository,
    TunnelSupervisor supervisor,
    ResolutionService resolutionService,
    SessionLogStore sessionLogStore)
{
    private readonly object _sync = new();
    private List<ForwardingConfig> _configs = [];

    public event EventHandler? ConfigsChanged;

    public IReadOnlyList<ForwardingConfig> Configs
    {
        get
        {
            lock (_sync)
            {
                return [.. _configs];
            }
        }
    }

    public ForwardingConfig? Find(ConfigId id)
    {
        lock (_sync)
        {
            return _configs.FirstOrDefault(c => c.Id == id);
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await repository.LoadAllAsync(cancellationToken);
        lock (_sync)
        {
            _configs = [.. loaded];
        }

        ConfigsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SaveAsync(ForwardingConfig config, CancellationToken cancellationToken = default)
    {
        await supervisor.StopAsync(config.Id);
        await repository.SaveAsync(config, cancellationToken);
        lock (_sync)
        {
            var index = _configs.FindIndex(c => c.Id == config.Id);
            if (index >= 0)
                _configs[index] = config;
            else
                _configs.Add(config);
        }

        ConfigsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task DeleteAsync(ConfigId id, CancellationToken cancellationToken = default)
    {
        await supervisor.StopAsync(id);
        await repository.DeleteAsync(id, cancellationToken);
        lock (_sync)
        {
            _configs.RemoveAll(c => c.Id == id);
        }

        resolutionService.Remove(id);
        sessionLogStore.Remove(id);
        ConfigsChanged?.Invoke(this, EventArgs.Empty);
    }
}
