using System.IO;
using LensTool.Translation;

namespace LensTool.Tests;

public class UsageTrackerTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"lenstool-usage-{Guid.NewGuid():N}.json");
    private DateTime _now = new(2026, 10, 25);

    public void Dispose() => File.Delete(_file);

    private UsageTracker Create() => new(_file, () => _now);

    [Fact]
    public void CountsPerEngineAndSurvivesRestart()
    {
        var tracker = Create();
        tracker.Add("Google", 100);
        tracker.Add("Google", 50);
        tracker.Add("DeepL", 7);

        var reloaded = Create();

        Assert.Equal(150, reloaded.Used("Google"));
        Assert.Equal(7, reloaded.Used("DeepL"));
        Assert.Equal(0, reloaded.Used("Other"));
    }

    [Fact]
    public void NewMonthStartsFromZero()
    {
        var tracker = Create();
        tracker.Add("Google", 100);

        _now = new DateTime(2026, 11, 1);

        Assert.Equal(0, tracker.Used("Google"));
        Assert.Equal(0, Create().Used("Google"));
    }

    [Fact]
    public void SetOverridesLocalCount()
    {
        var tracker = Create();
        tracker.Add("DeepL", 10);

        tracker.Set("DeepL", 12345);

        Assert.Equal(12345, Create().Used("DeepL"));
    }

    [Fact]
    public void CorruptFileStartsFromZero()
    {
        File.WriteAllText(_file, "not json");

        var tracker = Create();
        tracker.Add("Google", 5);

        Assert.Equal(5, tracker.Used("Google"));
    }
}
