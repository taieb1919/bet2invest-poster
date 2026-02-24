using global::Telegram.Bot;
using global::Telegram.Bot.Args;
using global::Telegram.Bot.Exceptions;
using global::Telegram.Bot.Requests.Abstractions;
using global::Telegram.Bot.Types;

namespace Bet2InvestPoster.Tests.Telegram.Commands;

/// <summary>Fake ITelegramBotClient that captures sent messages and chat IDs for assertions.</summary>
internal class FakeTelegramBotClient : ITelegramBotClient
{
    public List<string> SentMessages { get; } = [];
    public List<long> SentChatIds { get; } = [];

    public long BotId => 1234567890;
    public bool LocalBotServer => false;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(100);
    public IExceptionParser ExceptionsParser { get; set; } = new DefaultExceptionParser();

#pragma warning disable CS0067
    public event AsyncEventHandler<ApiRequestEventArgs>? OnMakingApiRequest;
    public event AsyncEventHandler<ApiResponseEventArgs>? OnApiResponseReceived;
#pragma warning restore CS0067

    public Task<TResponse> SendRequest<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        if (request is global::Telegram.Bot.Requests.SendMessageRequest sendReq)
        {
            SentMessages.Add(sendReq.Text);
            SentChatIds.Add(sendReq.ChatId.Identifier ?? 0);
        }

        // Return default(TResponse) to avoid reliance on parameterless constructors
        return Task.FromResult(default(TResponse)!);

    }

    public Task<bool> TestApi(CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task DownloadFile(string filePath, Stream destination,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task DownloadFile(TGFile file, Stream destination,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
