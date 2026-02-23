using Microsoft.Extensions.Logging.Abstractions;

namespace Bet2InvestPoster.Tests;

public class WorkerTests
{
    [Fact]
    public void Worker_CanBeInstantiated()
    {
        var logger = NullLogger<Worker>.Instance;
        var worker = new Worker(logger);

        Assert.NotNull(worker);
    }
}
