using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using LensTool.Translation;
using LensTool.Translation.Offline;

namespace LensTool.Tests;

public class OfflineTranslatorTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Theory]
    [InlineData("Hello world", "en")]
    [InlineData("Привет, мир", "ru")]
    [InlineData("Нажмите Save, чтобы сохранить файл", "ru")]
    [InlineData("12345", "en")]
    public void DetectsLanguageByScript(string text, string expected) =>
        Assert.Equal(expected, OfflineTranslator.DetectLanguage(text));

    [Fact]
    public void SplitsParagraphsAndSentences()
    {
        var parts = OfflineTranslator.SplitSentences("First one. Second one! Version 2.0 is out\nNew paragraph.");

        Assert.Equal(2, parts.Count);
        Assert.Equal(["First one.", "Second one!", "Version 2.0 is out"], parts[0]);
        Assert.Equal(["New paragraph."], parts[1]);
    }

    [Fact]
    public async Task TextAlreadyInTargetLanguageIsReturnedAsIs()
    {
        var translator = new OfflineTranslator(new ModelStore(Path.GetTempPath(), new HttpClient(new FakeHandler(HttpStatusCode.NotFound, ""))));

        var result = await translator.TranslateAsync("Привет", "RU");

        Assert.Equal(new TranslationResult("Привет", "RU", "RU"), result);
    }

    [Fact]
    public async Task UnsupportedPairIsUnavailable()
    {
        var translator = new OfflineTranslator(new ModelStore(Path.GetTempPath(), new HttpClient(new FakeHandler(HttpStatusCode.NotFound, ""))));

        await Assert.ThrowsAsync<TranslatorUnavailableException>(() => translator.TranslateAsync("Hello", "DE"));
    }

    /// <summary>Real models: set LENSTOOL_TEST_MODELS to a folder with opus-mt-en-ru / opus-mt-ru-en.</summary>
    [EnvironmentFact("LENSTOOL_TEST_MODELS")]
    public async Task TranslatesEnglishToRussianAndBack()
    {
        string folder = Environment.GetEnvironmentVariable("LENSTOOL_TEST_MODELS")!;
        using var translator = new OfflineTranslator(new ModelStore(folder, new HttpClient()));

        var ru = await translator.TranslateAsync("Good morning. The file was saved successfully.", "RU");
        var en = await translator.TranslateAsync("Сегодня хорошая погода.", "EN-US");

        output.WriteLine(ru.Text);
        output.WriteLine(en.Text);
        Assert.Equal("EN", ru.DetectedSourceLanguage);
        Assert.Contains("утр", ru.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("файл", ru.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("RU", en.DetectedSourceLanguage);
        Assert.Contains("weather", en.Text, StringComparison.OrdinalIgnoreCase);
    }
}

public class ModelStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"lenstool-models-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static OpusModel TinyModel(string content, string? sha = null) => new("en", "ru", "test/repo", "rev",
        [new ModelFile("onnx/file.bin", Encoding.UTF8.GetByteCount(content),
            sha ?? Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content))))]);

    [Fact]
    public async Task DownloadsVerifiesAndReportsProgress()
    {
        var model = TinyModel("model-bytes");
        var handler = new FakeHandler(HttpStatusCode.OK, "model-bytes");
        var store = new ModelStore(_root, new HttpClient(handler));
        var events = new List<string>();
        store.DownloadStarted += m => events.Add("start " + m.Id);
        store.DownloadFinished += (m, ex) => events.Add($"finish {m.Id} {ex is null}");

        string folder = await store.EnsureAsync(model);

        Assert.Equal("https://huggingface.co/test/repo/resolve/rev/onnx/file.bin", handler.Request!.RequestUri!.ToString());
        Assert.Equal("model-bytes", File.ReadAllText(Path.Combine(folder, "file.bin")));
        Assert.True(store.IsInstalled(model));
        Assert.Equal(["start opus-mt-en-ru", "finish opus-mt-en-ru True"], events);
    }

    [Fact]
    public async Task HashMismatchIsRejectedAndLeavesNoFile()
    {
        var model = TinyModel("model-bytes", sha: new string('0', 64));
        var store = new ModelStore(_root, new HttpClient(new FakeHandler(HttpStatusCode.OK, "model-bytes")));

        await Assert.ThrowsAsync<InvalidDataException>(() => store.EnsureAsync(model));

        Assert.False(store.IsInstalled(model));
        Assert.Empty(Directory.GetFiles(store.FolderOf(model)));
    }

    [Fact]
    public async Task InstalledModelIsNotDownloadedAgain()
    {
        var model = TinyModel("model-bytes");
        Directory.CreateDirectory(Path.Combine(_root, model.Id));
        File.WriteAllText(Path.Combine(_root, model.Id, "file.bin"), "model-bytes");
        var handler = new FakeHandler(new HttpRequestException("must not be called"));

        await new ModelStore(_root, new HttpClient(handler)).EnsureAsync(model);

        Assert.Null(handler.Request);
    }
}
