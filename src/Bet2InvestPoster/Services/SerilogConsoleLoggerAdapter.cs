using JTDev.Bet2InvestScraper.Logging;
using Microsoft.Extensions.Logging;

namespace Bet2InvestPoster.Services;

/// <summary>
/// Bridges the scraper's IConsoleLogger to Microsoft.Extensions.Logging so that
/// Bet2InvestClient can be registered in the DI container.
/// </summary>
internal sealed class SerilogConsoleLoggerAdapter : IConsoleLogger
{
    private readonly ILogger _logger;

    public SerilogConsoleLoggerAdapter(ILogger logger) => _logger = logger;

    public void Info(string message) => _logger.LogInformation("{Message}", message);
    public void Success(string message) => _logger.LogInformation("[OK] {Message}", message);
    public void Warning(string message) => _logger.LogWarning("{Message}", message);
    public void Error(string message) => _logger.LogError("{Message}", message);
    public void Header(string title) => _logger.LogInformation("=== {Title} ===", title);
}
