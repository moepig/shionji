using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shionji.Domain.Configuration;
using Shionji.Domain.Ports;
using Shionji.Domain.ValueObjects;

namespace Shionji.Infrastructure.Storage;

/// <summary>%APPDATA%/Shionji/configs.json への JSON 永続化。</summary>
public sealed class JsonForwardingConfigRepository(
    string filePath,
    ILogger<JsonForwardingConfigRepository>? logger = null) : IForwardingConfigRepository
{
    private readonly ILogger _log = logger ?? NullLogger<JsonForwardingConfigRepository>.Instance;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public static string DefaultPath => Path.Combine(StorageLocations.DefaultDirectory, "configs.json");

    public async Task<IReadOnlyList<ForwardingConfig>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadDocumentAsync(cancellationToken);

            // 変換できないエントリは無視する (手編集による破損で全体を道連れにしない)
            return [.. document.Configs
                .Select(StorageMapping.ToDomain)
                .Where(r => r.IsSuccess)
                .Select(r => r.Value)];
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task SaveAsync(ForwardingConfig config, CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadDocumentAsync(cancellationToken);
            var index = document.Configs.FindIndex(c => c.Id == config.Id.Value);
            var dto = StorageMapping.ToDto(config);
            if (index >= 0)
                document.Configs[index] = dto;
            else
                document.Configs.Add(dto);

            await WriteDocumentAsync(document, cancellationToken);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task DeleteAsync(ConfigId id, CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadDocumentAsync(cancellationToken);
            if (document.Configs.RemoveAll(c => c.Id == id.Value) > 0)
                await WriteDocumentAsync(document, cancellationToken);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// 読み込めないファイルは退避して空から続行する。
    /// そのまま例外を投げると一覧が黙って空になるうえ、以後の保存もすべて失敗するため。
    /// </summary>
    private async Task<ConfigsDocument> LoadDocumentAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
            return new ConfigsDocument();

        try
        {
            await using var stream = File.OpenRead(filePath);
            return await JsonSerializer.DeserializeAsync<ConfigsDocument>(stream, JsonOptions, cancellationToken)
                ?? new ConfigsDocument();
        }
        catch (JsonException ex)
        {
            QuarantineCorruptFile(ex);
            return new ConfigsDocument();
        }
    }

    private void QuarantineCorruptFile(Exception cause)
    {
        var quarantinePath = $"{filePath}.corrupt-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            File.Move(filePath, quarantinePath, overwrite: true);
            _log.LogError(
                cause,
                "設定ファイル {Path} を読み込めません。{Quarantine} へ退避し、空の設定で続行します。",
                filePath, quarantinePath);
        }
        catch (IOException moveFailure)
        {
            _log.LogError(moveFailure, "破損した設定ファイル {Path} の退避に失敗しました。", filePath);
        }
    }

    private async Task WriteDocumentAsync(ConfigsDocument document, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (directory is { Length: > 0 })
            Directory.CreateDirectory(directory);

        // 途中クラッシュで壊れないよう一時ファイルへ書いてから置き換える
        var tempPath = filePath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
        }

        File.Move(tempPath, filePath, overwrite: true);
    }
}
