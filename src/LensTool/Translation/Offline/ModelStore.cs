using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace LensTool.Translation.Offline;

/// <summary>
/// Keeps offline models in %LOCALAPPDATA%\LensTool\Models and downloads a missing model on first use.
/// Every downloaded file is checked against its pinned size and SHA-256 before it is used.
/// </summary>
public sealed class ModelStore(string rootFolder, HttpClient http)
{
    public static string DefaultFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LensTool", "Models");

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    /// <summary>Raised (on a background thread) before a model download starts.</summary>
    public event Action<OpusModel>? DownloadStarted;

    /// <summary>Raised after a download attempt; the exception is null on success.</summary>
    public event Action<OpusModel, Exception?>? DownloadFinished;

    public string FolderOf(OpusModel model) => Path.Combine(rootFolder, model.Id);

    /// <summary>All files are present with the expected sizes (hashes are verified at download time).</summary>
    public bool IsInstalled(OpusModel model) =>
        model.Files.All(f => new FileInfo(Path.Combine(FolderOf(model), f.FileName)) is { Exists: true } info && info.Length == f.Size);

    /// <summary>Returns the model folder, downloading missing files first.</summary>
    public async Task<string> EnsureAsync(OpusModel model, CancellationToken cancellationToken = default)
    {
        if (IsInstalled(model))
            return FolderOf(model);

        var gate = _locks.GetOrAdd(model.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (IsInstalled(model)) // another caller finished the download while we waited
                return FolderOf(model);

            DownloadStarted?.Invoke(model);
            try
            {
                await DownloadAsync(model, cancellationToken);
            }
            catch (Exception ex)
            {
                DownloadFinished?.Invoke(model, ex);
                throw;
            }
            DownloadFinished?.Invoke(model, null);
            return FolderOf(model);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task DownloadAsync(OpusModel model, CancellationToken ct)
    {
        string folder = FolderOf(model);
        Directory.CreateDirectory(folder);
        AppLog.Info($"Скачиваю модель {model.Id} ({model.TotalSize / 1048576.0:0} МБ) из {model.Repo}@{model.Revision[..Math.Min(8, model.Revision.Length)]}");

        foreach (var file in model.Files)
        {
            string target = Path.Combine(folder, file.FileName);
            if (new FileInfo(target) is { Exists: true } existing && existing.Length == file.Size)
                continue;

            string partial = target + ".part";
            try
            {
                await DownloadFileAsync(model.DownloadUri(file), partial, file, ct);
                File.Move(partial, target, overwrite: true);
            }
            finally
            {
                if (File.Exists(partial))
                    File.Delete(partial);
            }
        }
        AppLog.Info($"Модель {model.Id} скачана");
    }

    private async Task DownloadFileAsync(Uri uri, string path, ModelFile file, CancellationToken ct)
    {
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long total = 0;
        await using (var source = await response.Content.ReadAsStreamAsync(ct))
        await using (var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
        {
            var buffer = new byte[1 << 16];
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                total += read;
            }
        }

        string actual = Convert.ToHexStringLower(hash.GetHashAndReset());
        if (total != file.Size || actual != file.Sha256)
            throw new InvalidDataException(
                $"Файл {file.RepoPath} повреждён или изменён: {total} байт, SHA-256 {actual[..12]}… (ожидалось {file.Size} байт, {file.Sha256[..12]}…)");
    }
}
