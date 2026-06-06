using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;

namespace LabelForge.App.AI;

public sealed class ModelDownloadService
{
    private static readonly HttpClient http = new()
    {
        Timeout = TimeSpan.FromMinutes(30)
    };

    static ModelDownloadService()
    {
        http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("LabelForge", "1.0"));
    }

    /// <summary>
    /// Downloads the preset model to %AppData%\LabelForge\models\.
    /// Reports progress as 0–100 via <paramref name="progress"/>.
    /// Returns the local file path on success.
    /// </summary>
    public async Task<string> DownloadAsync(
        PresetModel model,
        IProgress<(int percent, string status)> progress,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(PresetModels.ModelsFolder);
        var dest = PresetModels.LocalPath(model);
        var tmp  = dest + ".tmp";

        progress.Report((0, $"Kapcsolódás: {model.FileName}…"));

        using var response = await http.GetAsync(model.DownloadUrl,
            HttpCompletionOption.ResponseHeadersRead, ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"HTTP {(int)response.StatusCode} – {response.ReasonPhrase}\n" +
                $"URL: {model.DownloadUrl}");

        long total = response.Content.Headers.ContentLength ?? -1;

        await using (var src  = await response.Content.ReadAsStreamAsync(ct))
        await using (var file = File.Create(tmp))
        {
            var buf = new byte[81920];
            long downloaded = 0;
            int read;
            while ((read = await src.ReadAsync(buf, ct)) > 0)
            {
                await file.WriteAsync(buf.AsMemory(0, read), ct);
                downloaded += read;

                int pct = total > 0 ? (int)(downloaded * 100 / total) : -1;
                string mb = $"{downloaded / 1_048_576.0:F1} MB"
                          + (total > 0 ? $" / {total / 1_048_576.0:F1} MB" : "");
                progress.Report((Math.Max(0, pct), $"{model.FileName}  {mb}"));
            }
        }

        // Atomic rename
        if (File.Exists(dest)) File.Delete(dest);
        File.Move(tmp, dest);

        progress.Report((100, $"Kész: {model.FileName}"));
        return dest;
    }

    public async Task<(string encoderPath, string decoderPath)> DownloadAndExtractSamPackageAsync(
        PresetModel model,
        IProgress<(int percent, string status)> progress,
        CancellationToken ct = default)
    {
        if (model.Kind != YoloKind.SamPackage)
            throw new ArgumentException("A kiválasztott preset nem SAM csomag.", nameof(model));

        var zipPath = await DownloadAsync(model, progress, ct);
        var extractDir = PresetModels.PackageFolder(model);
        var tmpDir = extractDir + ".tmp";

        progress.Report((100, $"Kicsomagolás: {model.FileName}"));

        if (Directory.Exists(tmpDir))
            Directory.Delete(tmpDir, recursive: true);
        Directory.CreateDirectory(tmpDir);

        try
        {
            ZipFile.ExtractToDirectory(zipPath, tmpDir, overwriteFiles: true);

            if (Directory.Exists(extractDir))
                Directory.Delete(extractDir, recursive: true);
            Directory.Move(tmpDir, extractDir);
        }
        finally
        {
            if (Directory.Exists(tmpDir))
                Directory.Delete(tmpDir, recursive: true);
        }

        var encoder = FindOnnx(extractDir, "encoder")
            ?? throw new FileNotFoundException("A csomagban nem található encoder ONNX fájl.");
        var decoder = FindOnnx(extractDir, "decoder")
            ?? throw new FileNotFoundException("A csomagban nem található decoder ONNX fájl.");

        progress.Report((100, $"Kész: {Path.GetFileName(encoder)} + {Path.GetFileName(decoder)}"));
        return (encoder, decoder);
    }

    private static string? FindOnnx(string folder, string namePart) =>
        Directory.EnumerateFiles(folder, "*.onnx", SearchOption.AllDirectories)
            .OrderBy(path => path.Length)
            .FirstOrDefault(path =>
                Path.GetFileName(path).Contains(namePart, StringComparison.OrdinalIgnoreCase));
}
