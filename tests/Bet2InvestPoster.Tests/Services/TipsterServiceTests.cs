using System.Text.Json;
using Bet2InvestPoster.Configuration;
using Bet2InvestPoster.Models;
using Bet2InvestPoster.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bet2InvestPoster.Tests.Services;

public class TipsterServiceTests : IDisposable
{
    private readonly string _tempDir;

    public TipsterServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, true);

    private TipsterService CreateService(string? dataPath = null) =>
        new(Options.Create(new PosterOptions { DataPath = dataPath ?? _tempDir }),
            NullLogger<TipsterService>.Instance);

    private void WriteTipsters(string json) =>
        File.WriteAllText(Path.Combine(_tempDir, "tipsters.json"), json);

    // --- 6.2: Valid file with multiple tipsters ---

    [Fact]
    public async Task LoadTipstersAsync_ValidFile_ReturnsParsedTipsters()
    {
        WriteTipsters("""
        [
            { "url": "https://bet2invest.com/tipsters/performance-stats/Alice", "name": "Alice" },
            { "url": "https://bet2invest.com/tipsters/performance-stats/Bob", "name": "Bob" }
        ]
        """);

        var result = await CreateService().LoadTipstersAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal("Alice", result[0].Name);
        Assert.Equal("https://bet2invest.com/tipsters/performance-stats/Alice", result[0].Url);
        Assert.Equal("Alice", result[0].Id);
        Assert.Equal("Bob", result[1].Name);
        Assert.Equal("Bob", result[1].Id);
    }

    // --- 6.3: Slug extraction from different URL formats ---

    [Theory]
    [InlineData("https://bet2invest.com/tipsters/performance-stats/NG1", "NG1")]
    [InlineData("https://bet2invest.com/tipsters/performance-stats/Edge_Analytics", "Edge_Analytics")]
    [InlineData("https://bet2invest.com/tipsters/performance-stats/Slug/", "Slug")]
    public async Task LoadTipstersAsync_ExtractsSlugFromVariousUrlFormats(string url, string expectedSlug)
    {
        WriteTipsters(JsonSerializer.Serialize(new[] { new { url, name = "Test" } }));

        var result = await CreateService().LoadTipstersAsync();

        Assert.Single(result);
        Assert.Equal(expectedSlug, result[0].Id);
    }

    // --- 6.4: File not found ---

    [Fact]
    public async Task LoadTipstersAsync_FileNotFound_ThrowsFileNotFoundException()
    {
        // Directory exists but tipsters.json does not
        var emptyDir = Path.Combine(_tempDir, "empty");
        Directory.CreateDirectory(emptyDir);
        var service = CreateService(emptyDir);

        await Assert.ThrowsAsync<FileNotFoundException>(() => service.LoadTipstersAsync());
    }

    [Fact]
    public async Task LoadTipstersAsync_DirectoryNotFound_ThrowsFileNotFoundException()
    {
        var service = CreateService(Path.Combine(_tempDir, "nonexistent"));

        await Assert.ThrowsAsync<FileNotFoundException>(() => service.LoadTipstersAsync());
    }

    // --- 6.5: Invalid JSON ---

    [Fact]
    public async Task LoadTipstersAsync_InvalidJson_ThrowsJsonException()
    {
        WriteTipsters("not valid json {{{");

        await Assert.ThrowsAsync<JsonException>(() => CreateService().LoadTipstersAsync());
    }

    // --- 6.6: Empty array ---

    [Fact]
    public async Task LoadTipstersAsync_EmptyArray_ReturnsEmptyList()
    {
        WriteTipsters("[]");

        var result = await CreateService().LoadTipstersAsync();

        Assert.Empty(result);
    }

    // --- 6.7: Invalid entries filtered ---

    [Fact]
    public async Task LoadTipstersAsync_InvalidEntries_FiltersThemOut()
    {
        WriteTipsters("""
        [
            { "url": "", "name": "EmptyUrl" },
            { "url": "https://bet2invest.com/tipsters/performance-stats/Valid", "name": "" },
            { "url": "not-a-valid-url", "name": "BadUrl" },
            { "url": "https://bet2invest.com/tipsters/performance-stats/Valid", "name": "Valid" }
        ]
        """);

        var result = await CreateService().LoadTipstersAsync();

        Assert.Single(result);
        Assert.Equal("Valid", result[0].Name);
        Assert.Equal("Valid", result[0].Id);
    }

    // --- 6.8: Hot-reload (re-read on every call) ---

    [Fact]
    public async Task LoadTipstersAsync_FileModifiedBetweenCalls_ReturnsUpdatedContent()
    {
        WriteTipsters("""[{ "url": "https://bet2invest.com/tipsters/performance-stats/First", "name": "First" }]""");
        var service = CreateService();

        var first = await service.LoadTipstersAsync();
        Assert.Single(first);
        Assert.Equal("First", first[0].Name);

        WriteTipsters("""
        [
            { "url": "https://bet2invest.com/tipsters/performance-stats/First", "name": "First" },
            { "url": "https://bet2invest.com/tipsters/performance-stats/Second", "name": "Second" }
        ]
        """);

        var second = await service.LoadTipstersAsync();
        Assert.Equal(2, second.Count);
    }

    // --- 6.9: DI Scoped registration ---

    [Fact]
    public void TipsterService_RegisteredAsScoped_DifferentInstancesPerScope()
    {
        var services = new ServiceCollection();
        services.Configure<PosterOptions>(o => o.DataPath = _tempDir);
        services.AddLogging();
        services.AddScoped<ITipsterService, TipsterService>();
        using var provider = services.BuildServiceProvider();

        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();
        var instance1 = scope1.ServiceProvider.GetRequiredService<ITipsterService>();
        var instance2 = scope2.ServiceProvider.GetRequiredService<ITipsterService>();

        Assert.NotSame(instance1, instance2);
    }

    // --- L4: DI descriptor — lifetime and implementation type ---

    [Fact]
    public void TipsterService_DiDescriptor_HasScopedLifetimeAndCorrectImplementation()
    {
        var services = new ServiceCollection();
        services.Configure<PosterOptions>(o => o.DataPath = _tempDir);
        services.AddLogging();
        services.AddScoped<ITipsterService, TipsterService>();

        var descriptor = services.Single(d => d.ServiceType == typeof(ITipsterService));

        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal(typeof(TipsterService), descriptor.ImplementationType);
    }

    // --- L2: JSON null string treated as empty list ---

    [Fact]
    public async Task LoadTipstersAsync_NullJsonContent_ReturnsEmptyList()
    {
        // "null" is syntactically valid JSON but deserializes to null
        WriteTipsters("null");

        var result = await CreateService().LoadTipstersAsync();

        Assert.Empty(result);
    }

    // --- 8.2: AddTipsterAsync ---

    [Fact]
    public async Task AddTipsterAsync_ValidUrl_AddsTipsterAndPersists()
    {
        WriteTipsters("[]");
        var service = CreateService();

        var result = await service.AddTipsterAsync(
            "https://bet2invest.com/tipsters/performance-stats/johndoe");

        Assert.Equal("https://bet2invest.com/tipsters/performance-stats/johndoe", result.Url);
        Assert.Equal("johndoe", result.Name);

        // Verify persistence
        var loaded = await service.LoadTipstersAsync();
        Assert.Single(loaded);
        Assert.Equal("johndoe", loaded[0].Name);
    }

    [Fact]
    public async Task AddTipsterAsync_DuplicateUrl_ThrowsInvalidOperationException()
    {
        WriteTipsters("""
        [{ "url": "https://bet2invest.com/tipsters/performance-stats/johndoe", "name": "johndoe" }]
        """);
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddTipsterAsync("https://bet2invest.com/tipsters/performance-stats/johndoe"));
    }

    [Fact]
    public async Task AddTipsterAsync_InvalidUrl_ThrowsArgumentException()
    {
        WriteTipsters("[]");
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AddTipsterAsync("not-a-valid-url"));
    }

    [Fact]
    public async Task AddTipsterAsync_WritesAtomically_TempFileUsedThenRenamed()
    {
        WriteTipsters("[]");
        var service = CreateService();
        var filePath = Path.Combine(_tempDir, "tipsters.json");
        var tempPath = filePath + ".tmp";

        await service.AddTipsterAsync(
            "https://bet2invest.com/tipsters/performance-stats/testslug");

        // The .tmp file should not exist after a successful write (renamed to .json)
        Assert.False(File.Exists(tempPath));
        Assert.True(File.Exists(filePath));

        // Verify content is valid JSON with the new entry
        var content = await File.ReadAllTextAsync(filePath);
        Assert.Contains("testslug", content);
    }

    // --- 8.2: RemoveTipsterAsync ---

    [Fact]
    public async Task RemoveTipsterAsync_ExistingUrl_RemovesTipsterAndPersists()
    {
        WriteTipsters("""
        [
            { "url": "https://bet2invest.com/tipsters/performance-stats/Alice", "name": "Alice" },
            { "url": "https://bet2invest.com/tipsters/performance-stats/Bob", "name": "Bob" }
        ]
        """);
        var service = CreateService();

        var result = await service.RemoveTipsterAsync(
            "https://bet2invest.com/tipsters/performance-stats/Alice");

        Assert.True(result);

        var loaded = await service.LoadTipstersAsync();
        Assert.Single(loaded);
        Assert.Equal("Bob", loaded[0].Name);
    }

    [Fact]
    public async Task RemoveTipsterAsync_UnknownUrl_ReturnsFalse()
    {
        WriteTipsters("""
        [{ "url": "https://bet2invest.com/tipsters/performance-stats/Alice", "name": "Alice" }]
        """);
        var service = CreateService();

        var result = await service.RemoveTipsterAsync(
            "https://bet2invest.com/tipsters/performance-stats/Unknown");

        Assert.False(result);
    }

    [Fact]
    public async Task RemoveTipsterAsync_WritesAtomically_TempFileCleanedUp()
    {
        WriteTipsters("""
        [{ "url": "https://bet2invest.com/tipsters/performance-stats/Alice", "name": "Alice" }]
        """);
        var service = CreateService();
        var filePath = Path.Combine(_tempDir, "tipsters.json");
        var tempPath = filePath + ".tmp";

        await service.RemoveTipsterAsync(
            "https://bet2invest.com/tipsters/performance-stats/Alice");

        Assert.False(File.Exists(tempPath));
        Assert.True(File.Exists(filePath));
    }
}
