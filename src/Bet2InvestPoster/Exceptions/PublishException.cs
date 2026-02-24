namespace Bet2InvestPoster.Exceptions;

/// <summary>
/// Raised when publishing a bet order to the bet2invest API fails.
/// </summary>
public class PublishException : Exception
{
    public int BetId { get; }
    public int HttpStatusCode { get; }

    public PublishException(int betId, int httpStatusCode, string message)
        : base(message)
    {
        BetId = betId;
        HttpStatusCode = httpStatusCode;
    }
}
