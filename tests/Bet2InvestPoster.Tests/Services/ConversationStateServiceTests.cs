using Bet2InvestPoster.Services;
using Telegram.Bot;

namespace Bet2InvestPoster.Tests.Services;

public class ConversationStateServiceTests
{
    // --- 7.3 : Register + TryGet ---

    [Fact]
    public void TryGet_AfterRegister_ReturnsCallbackThatInvokes()
    {
        var svc = new ConversationStateService();
        var invoked = false;
        Func<ITelegramBotClient, string, CancellationToken, Task> callback = (_, _, _) => { invoked = true; return Task.CompletedTask; };

        svc.Register(42L, callback);

        var found = svc.TryGet(42L, out var cb);
        _ = cb!(null!, "", default);

        Assert.True(found);
        Assert.True(invoked);
    }

    [Fact]
    public void TryGet_WithoutRegister_ReturnsFalse()
    {
        var svc = new ConversationStateService();

        var found = svc.TryGet(99L, out var cb);

        Assert.False(found);
        Assert.Null(cb);
    }

    // --- 7.3 : Clear ---

    [Fact]
    public void Clear_AfterRegister_RemovesState()
    {
        var svc = new ConversationStateService();
        svc.Register(10L, (_, _, _) => Task.CompletedTask);

        svc.Clear(10L);

        Assert.False(svc.TryGet(10L, out _));
    }

    [Fact]
    public void Clear_OnNonExistentChat_DoesNotThrow()
    {
        var svc = new ConversationStateService();

        var ex = Record.Exception(() => svc.Clear(999L));

        Assert.Null(ex);
    }

    // --- 7.3 : Routage — Second Register remplace le premier ---

    [Fact]
    public void Register_Twice_OnlySecondIsRetrievable()
    {
        var svc = new ConversationStateService();
        var firstInvoked = false;
        var secondInvoked = false;
        Func<ITelegramBotClient, string, CancellationToken, Task> first = (_, _, _) => { firstInvoked = true; return Task.CompletedTask; };
        Func<ITelegramBotClient, string, CancellationToken, Task> second = (_, _, _) => { secondInvoked = true; return Task.CompletedTask; };

        svc.Register(5L, first);
        svc.Register(5L, second);

        var found = svc.TryGet(5L, out var cb);
        _ = cb!(null!, "", default);

        Assert.True(found);
        Assert.False(firstInvoked);  // Premier callback remplacé
        Assert.True(secondInvoked);  // Second est actif
    }

    // --- 7.3 : Timeout — nettoyage automatique après expiration ---

    [Fact]
    public async Task Register_AfterTimeout_StateIsCleared()
    {
        var svc = new ConversationStateService();
        svc.Register(7L, (_, _, _) => Task.CompletedTask, TimeSpan.FromMilliseconds(50));

        await Task.Delay(200); // Attendre l'expiration du timeout

        Assert.False(svc.TryGet(7L, out _));
    }

    // --- 7.3 : Isolation entre chatIds ---

    [Fact]
    public void Register_ForDifferentChats_AreIsolated()
    {
        var svc = new ConversationStateService();
        svc.Register(1L, (_, _, _) => Task.CompletedTask);
        svc.Register(2L, (_, _, _) => Task.CompletedTask);

        svc.Clear(1L);

        Assert.False(svc.TryGet(1L, out _));
        Assert.True(svc.TryGet(2L, out _));
    }
}
