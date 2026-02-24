using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bet2InvestPoster.Tests;

public class WorkerTests
{
    [Fact]
    public void Worker_CanBeInstantiated()
    {
        var logger = NullLogger<Worker>.Instance;
        var services = new ServiceCollection();
        using var provider = services.BuildServiceProvider();
        var worker = new Worker(logger, provider);

        Assert.NotNull(worker);
    }
}
