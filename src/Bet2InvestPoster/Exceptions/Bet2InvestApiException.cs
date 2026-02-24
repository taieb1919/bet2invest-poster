namespace Bet2InvestPoster.Exceptions;

/// <summary>
/// Raised when the bet2invest API returns an unexpected HTTP response or a contract change is detected.
/// </summary>
public class Bet2InvestApiException : Exception
{
    public string Endpoint { get; }
    public int HttpStatusCode { get; }
    public string? ResponsePayload { get; }
    // True when the response shape differs from the expected contract (likely an API change).
    public bool DetectedChange { get; }

    public Bet2InvestApiException(
        string endpoint,
        int httpStatusCode,
        string? responsePayload = null,
        bool detectedChange = false)
        : base($"API error {httpStatusCode} on {endpoint}")
    {
        Endpoint = endpoint;
        HttpStatusCode = httpStatusCode;
        ResponsePayload = responsePayload;
        DetectedChange = detectedChange;
    }
}
