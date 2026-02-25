using Telegram.Bot;

namespace Bet2InvestPoster.Services;

/// <summary>
/// Gère les conversations stateful multi-tour pour les commandes Telegram nécessitant
/// une confirmation utilisateur (ex: /tipsters update).
/// Singleton — état partagé entre tous les scopes.
/// </summary>
public interface IConversationStateService
{
    /// <summary>
    /// Enregistre un callback en attente de réponse pour le chatId donné.
    /// Remplace tout état précédent. Nettoie automatiquement après <paramref name="timeout"/>.
    /// </summary>
    void Register(long chatId, Func<ITelegramBotClient, string, CancellationToken, Task> callback, TimeSpan? timeout = null);

    /// <summary>
    /// Tente de récupérer le callback en attente pour le chatId.
    /// Retourne true si un état existe et fournit le callback via <paramref name="callback"/>.
    /// </summary>
    bool TryGet(long chatId, out Func<ITelegramBotClient, string, CancellationToken, Task>? callback);

    /// <summary>
    /// Supprime et annule l'état de conversation pour le chatId.
    /// </summary>
    void Clear(long chatId);
}
