# Story 2.1 : ExtendedBet2InvestClient — Authentification et Wrapper

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a le système,
I want m'authentifier automatiquement sur l'API bet2invest et disposer d'un client étendu,
So that je puisse accéder aux endpoints de paris à venir et de publication non couverts par le scraper.

## Acceptance Criteria

1. **Given** le `Bet2InvestClient` du submodule scraper référencé dans le projet
   **When** le service démarre un cycle d'exécution
   **Then** `ExtendedBet2InvestClient` compose `Bet2InvestClient` et expose `LoginAsync()`, `GetUpcomingBetsAsync()`, `PublishBetAsync()`
   **And** l'authentification est automatique via les credentials configurés (FR2)

2. **Given** un token d'authentification existant
   **When** le token est expiré avant ou pendant une exécution
   **Then** le token est renouvelé automatiquement sans intervention (FR3)

3. **Given** le wrapper `ExtendedBet2InvestClient`
   **When** il est enregistré dans le conteneur DI
   **Then** le code API est isolé dans `Services/ExtendedBet2InvestClient.cs` avec interface `IExtendedBet2InvestClient` (NFR11)

4. **Given** toute requête à l'API bet2invest
   **When** plusieurs requêtes sont envoyées séquentiellement
   **Then** un délai de 500ms minimum est respecté entre chaque requête API (NFR8)

## Tasks / Subtasks

- [x] Task 1 : Créer l'interface `IExtendedBet2InvestClient` (AC: #1, #3)
  - [x] 1.1 Créer `Services/IExtendedBet2InvestClient.cs` avec les signatures : `LoginAsync()`, `GetUpcomingBetsAsync(int tipsterId)`, `PublishBetAsync(bet)`, `IsAuthenticated`
  - [x] 1.2 Définir les types de retour appropriés basés sur les modèles du submodule (`SettledBet` réutilisable pour les upcoming bets, ou nouveau DTO si la structure diffère)

- [x] Task 2 : Implémenter `ExtendedBet2InvestClient` — Authentification (AC: #1, #2, #4)
  - [x] 2.1 Créer `Services/ExtendedBet2InvestClient.cs` qui compose `Bet2InvestClient` du submodule
  - [x] 2.2 Injecter `IOptions<Bet2InvestOptions>` pour les credentials (`Identifier`, `Password`, `ApiBase`, `RequestDelayMs`)
  - [x] 2.3 Implémenter `LoginAsync()` qui délègue à `Bet2InvestClient.LoginAsync(identifier, password)`
  - [x] 2.4 Implémenter le tracking d'expiration du token via `LoginResponse.ExpiresIn` (stocker `_tokenExpiresAt = DateTime.UtcNow.AddSeconds(expiresIn)` avec marge de sécurité de 60s)
  - [x] 2.5 Implémenter `EnsureAuthenticatedAsync()` — méthode privée qui vérifie `IsAuthenticated` ET `_tokenExpiresAt > DateTime.UtcNow`, re-login si nécessaire (FR3)
  - [x] 2.6 Appeler `EnsureAuthenticatedAsync()` au début de chaque méthode publique (`GetUpcomingBetsAsync`, `PublishBetAsync`)
  - [x] 2.7 Implémenter le rate limiting : `await Task.Delay(RequestDelayMs)` avant chaque requête HTTP (NFR8)

- [x] Task 3 : Implémenter `GetUpcomingBetsAsync()` (AC: #1)
  - [x] 3.1 Identifier l'endpoint API pour les paris à venir — patron probable basé sur le scraper : `GET /v1/statistics/{tipsterId}/bets/upcoming` (à confirmer par spike)
  - [x] 3.2 Implémenter l'appel HTTP avec le `HttpClient` interne de `Bet2InvestClient` (accès via composition)
  - [x] 3.3 Gérer la pagination si l'API la supporte (même pattern `Pagination` que le scraper)
  - [x] 3.4 Désérialiser avec `System.Text.Json` (case-insensitive, même config que le scraper)
  - [x] 3.5 Loguer l'opération avec Step `Scrape` : nombre de paris récupérés, tipster ID

- [x] Task 4 : Implémenter `PublishBetAsync()` (AC: #1)
  - [x] 4.1 Identifier l'endpoint API pour la publication — spike nécessaire pour déterminer le contrat exact
  - [x] 4.2 Implémenter l'appel HTTP POST avec le body attendu par l'API
  - [x] 4.3 Loguer l'opération avec Step `Publish` : betId, résultat (succès/échec)
  - [x] 4.4 Lever `Bet2InvestApiException` en cas d'erreur (code HTTP inattendu, changement d'API — NFR9)

- [x] Task 5 : Enregistrement DI et intégration (AC: #3)
  - [x] 5.1 Enregistrer `Bet2InvestClient` en **Singleton** dans `Program.cs` (réutilisé entre cycles — architecture decision)
  - [x] 5.2 Enregistrer `IExtendedBet2InvestClient` / `ExtendedBet2InvestClient` en **Scoped** (un scope par cycle d'exécution)
  - [x] 5.3 Le constructeur de `ExtendedBet2InvestClient` prend : `Bet2InvestClient`, `IOptions<Bet2InvestOptions>`, `ILogger<ExtendedBet2InvestClient>`

- [x] Task 6 : Créer les exceptions custom (AC: #1)
  - [x] 6.1 Créer `Exceptions/Bet2InvestApiException.cs` — propriétés : `Endpoint`, `HttpStatusCode`, `ResponsePayload`, `DetectedChange` (NFR9)
  - [x] 6.2 Créer `Exceptions/PublishException.cs` — propriétés : `BetId`, `HttpStatusCode`, `Message`

- [x] Task 7 : Tests unitaires (tous les ACs)
  - [x] 7.1 Créer `tests/Bet2InvestPoster.Tests/Services/ExtendedBet2InvestClientTests.cs`
  - [x] 7.2 Tester `EnsureAuthenticatedAsync` — login initial quand pas authentifié
  - [x] 7.3 Tester le renouvellement automatique du token expiré (FR3)
  - [x] 7.4 Tester que le rate limiting respecte le délai configuré (NFR8)
  - [x] 7.5 Tester que `Bet2InvestApiException` est levée sur code HTTP inattendu (NFR9)
  - [x] 7.6 Vérifier 0 régression : les 11 tests existants doivent toujours passer

## Dev Notes

### Exigences Techniques Critiques

**Composition, PAS héritage :**
Le `Bet2InvestClient` du submodule a un constructeur `(string apiBase, int requestDelayMs, IConsoleLogger logger)` — il n'est PAS conçu pour l'héritage (pas de méthodes `virtual`/`protected`). L'architecture impose la **composition** : `ExtendedBet2InvestClient` encapsule une instance de `Bet2InvestClient` comme champ privé.

**Problème d'accès au HttpClient interne :**
Le `Bet2InvestClient` du scraper utilise un `HttpClient` interne privé pour ses requêtes. Pour `GetUpcomingBetsAsync()` et `PublishBetAsync()`, l'`ExtendedBet2InvestClient` a deux options :
1. **Option recommandée** : Créer son propre `HttpClient` interne, récupérer le Bearer token après login via `Bet2InvestClient`, et l'utiliser pour les nouveaux endpoints
2. **Option alternative** : Utiliser directement `Bet2InvestClient.LoginAsync()` pour l'auth, puis gérer les appels HTTP indépendamment

**Attention :** Le `Bet2InvestClient` du scraper stocke le token dans `_httpClient.DefaultRequestHeaders.Authorization` après login. Ce header n'est PAS accessible publiquement. Il faudra soit :
- Appeler `LoginAsync()` sur le `Bet2InvestClient` ET capturer le token retourné par l'API (re-call login endpoint) pour notre propre HttpClient
- OU modifier l'approche : `ExtendedBet2InvestClient` gère sa propre auth complètement, en utilisant directement l'endpoint `POST /auth/login` avec son propre HttpClient, sans passer par `Bet2InvestClient.LoginAsync()`

**Recommandation architecturale :** `ExtendedBet2InvestClient` devrait gérer sa propre authentification HTTP directement (son propre `HttpClient` + appel `POST /auth/login`), tout en réutilisant les **modèles** du submodule (`LoginRequest`, `LoginResponse`, `Tipster`, `SettledBet`, `Pagination`, etc.). Le `Bet2InvestClient` du submodule ne sera utilisé directement que si nécessaire pour des méthodes déjà implémentées (`GetTipstersAsync`, `GetSettledBetsAsync`).

**Token Expiration :**
`LoginResponse.ExpiresIn` retourne le nombre de secondes de validité. Stocker `_tokenExpiresAt = DateTime.UtcNow.AddSeconds(expiresIn - 60)` (marge de 60s) pour garantir le renouvellement avant expiration réelle. Ne JAMAIS persister le token sur disque.

**Rate Limiting :**
Le scraper implémente déjà un délai + jitter aléatoire (100-500ms) entre requêtes. `ExtendedBet2InvestClient` doit implémenter son propre rate limiting car il a son propre `HttpClient`. Utiliser `Bet2InvestOptions.RequestDelayMs` (défaut 500ms).

**Logging :**
Toutes les opérations doivent utiliser `ILogger<ExtendedBet2InvestClient>` avec la propriété `Step` :
- `Log.ForContext("Step", "Auth")` pour login/token renewal
- `Log.ForContext("Step", "Scrape")` pour GetUpcomingBetsAsync
- `Log.ForContext("Step", "Publish")` pour PublishBetAsync
- JAMAIS loguer les credentials ou le token (NFR5)

### Conformité Architecture

**Décisions architecturales à respecter impérativement :**

| Décision | Valeur | Source |
|---|---|---|
| Pattern composition | `ExtendedBet2InvestClient` compose, ne modifie JAMAIS le submodule | [Architecture: Core Decisions] |
| DI Lifetime | `Bet2InvestClient` = Singleton, `ExtendedBet2InvestClient` = Scoped | [Architecture: DI Pattern] |
| Sérialisation | `System.Text.Json` uniquement — JAMAIS Newtonsoft | [Architecture: Data] |
| JSON naming | `camelCase` (défaut System.Text.Json) | [Architecture: Format Patterns] |
| Dates | ISO 8601 : `"2026-02-23T08:00:00Z"` | [Architecture: Format Patterns] |
| Nommage C# | PascalCase classes/méthodes, camelCase locals, préfixe `I` interfaces | [Architecture: Naming] |
| Fichiers | Un fichier = une classe, nom fichier = nom classe | [Architecture: Naming] |
| Dossiers | PascalCase — `Services/`, `Exceptions/` | [Architecture: Naming] |
| Interface par service | Obligatoire — `IExtendedBet2InvestClient` | [Architecture: Structure] |
| Error handling | Exceptions custom, JAMAIS de catch silencieux | [Architecture: Process Patterns] |
| Auth flow | Login au début de chaque cycle, re-login auto si expiré, token en mémoire uniquement | [Architecture: Authentication] |
| Rate limiting | 500ms entre chaque requête API bet2invest | [Architecture: API Patterns, NFR8] |
| Credentials | JAMAIS dans les logs, JAMAIS dans les Properties Serilog | [Architecture: Security, NFR5] |

**Boundaries à ne PAS violer :**
- `ExtendedBet2InvestClient` est le seul point de contact avec l'API bet2invest pour les nouveaux endpoints (upcoming bets, publish)
- Aucun appel HTTP direct à l'API bet2invest en dehors de `Services/`
- Les Workers ne contiennent JAMAIS de logique métier — uniquement orchestration
- Les modèles du submodule sont réutilisés directement, PAS dupliqués

### Librairies et Frameworks — Exigences Spécifiques

**Packages déjà installés (NE PAS ajouter de nouveaux packages pour cette story) :**

| Package | Version | Usage dans cette story |
|---|---|---|
| Serilog 4.3.1 | Déjà installé | Logging avec `ILogger<ExtendedBet2InvestClient>` et propriété `Step` |
| System.Text.Json | Inclus .NET 9 | Désérialisation réponses API (LoginResponse, bets, etc.) |
| Microsoft.Extensions.Options | Inclus .NET 9 | `IOptions<Bet2InvestOptions>` pour credentials et config |

**HttpClient — Pattern à suivre :**
- Créer un `HttpClient` dans le constructeur de `ExtendedBet2InvestClient` (pas via IHttpClientFactory — aligné avec le pattern du submodule)
- Configurer `BaseAddress` depuis `Bet2InvestOptions.ApiBase` (défaut : `https://api.bet2invest.com`)
- Timeout : 30 secondes (aligné avec le scraper)
- Implémenter `IDisposable` pour cleanup du `HttpClient`
- **NE PAS** utiliser `HttpClientFactory` — le client est Scoped et géré par le conteneur DI

**Modèles du submodule à réutiliser (namespace `JTDev.Bet2InvestScraper`) :**

| Modèle | Localisation | Usage |
|---|---|---|
| `LoginRequest` | `Models/Bet2InvestModels.cs` | Body du POST `/auth/login` |
| `LoginResponse` | `Models/Bet2InvestModels.cs` | Parsing réponse login (AccessToken, ExpiresIn) |
| `Tipster` | `Models/Bet2InvestModels.cs` | Référence tipster ID pour upcoming bets |
| `SettledBet` | `Models/Bet2InvestModels.cs` | Base probable pour upcoming bets (même structure API) |
| `BetEvent` | `Models/Bet2InvestModels.cs` | Info match (Home, Away, Starts) |
| `BetSport` | `Models/Bet2InvestModels.cs` | Info sport |
| `BetLeague` | `Models/Bet2InvestModels.cs` | Info league |
| `Pagination` | `Models/Bet2InvestModels.cs` | Gestion pagination API |

**System.Text.Json — Configuration :**
```csharp
private static readonly JsonSerializerOptions JsonOptions = new()
{
    PropertyNameCaseInsensitive = true
};
```
Aligné avec le pattern du scraper (`PropertyNameCaseInsensitive = true`). Les modèles du submodule utilisent déjà `[JsonPropertyName("camelCase")]`.

**Serilog — Pattern de logging avec Step :**
```csharp
// Utiliser LogContext.PushProperty pour injecter le Step
using (LogContext.PushProperty("Step", "Auth"))
{
    _logger.LogInformation("Login réussi pour {ApiBase}", _options.ApiBase);
}
```
**ATTENTION :** Ne pas utiliser `Log.ForContext()` (API statique Serilog) — utiliser `LogContext.PushProperty()` car le projet utilise `ILogger<T>` de Microsoft.Extensions.Logging via Serilog.Extensions.Hosting. Vérifier que `Enrich.FromLogContext()` est configuré dans Program.cs (déjà fait en story 1.2).

### Structure des Fichiers

**Fichiers à CRÉER dans cette story :**

```
src/Bet2InvestPoster/
├── Services/
│   ├── IExtendedBet2InvestClient.cs    ← NOUVEAU (interface)
│   ├── BetOrderRequest.cs              ← NOUVEAU (DTO publication)
│   ├── SerilogConsoleLoggerAdapter.cs  ← NOUVEAU (adaptateur DI interne)
│   └── ExtendedBet2InvestClient.cs     ← NOUVEAU (implémentation)
└── Exceptions/
    ├── Bet2InvestApiException.cs        ← NOUVEAU
    └── PublishException.cs              ← NOUVEAU

tests/Bet2InvestPoster.Tests/
└── Services/
    └── ExtendedBet2InvestClientTests.cs ← NOUVEAU
```

**Fichiers à MODIFIER dans cette story :**

```
src/Bet2InvestPoster/
├── Program.cs                           ← MODIFIER (ajout DI registrations)
└── Bet2InvestPoster.csproj              ← MODIFIER (InternalsVisibleTo tests)
```

**Fichiers à NE PAS TOUCHER :**

```
jtdev-bet2invest-scraper/                ← SUBMODULE — INTERDIT de modifier
src/Bet2InvestPoster/
├── Worker.cs                            ← Pas de logique métier dans les Workers
├── Configuration/                       ← Options déjà définies en story 1.2
│   ├── Bet2InvestOptions.cs             ← NE PAS modifier
│   ├── TelegramOptions.cs              ← NE PAS modifier
│   └── PosterOptions.cs                ← NE PAS modifier
├── appsettings.json                     ← NE PAS modifier (config déjà complète)
└── appsettings.Development.json         ← NE PAS modifier

tests/Bet2InvestPoster.Tests/
├── UnitTest1.cs                         ← NE PAS modifier (test existant)
└── Configuration/
    └── OptionsTests.cs                  ← NE PAS modifier (tests existants)
```

**Dossiers à créer si inexistants :**
- `src/Bet2InvestPoster/Services/` — premier service du projet
- `src/Bet2InvestPoster/Exceptions/` — premières exceptions custom
- `tests/Bet2InvestPoster.Tests/Services/` — premier dossier de tests services

**Conventions de nommage fichiers :**
- Interface : `I` + nom classe → `IExtendedBet2InvestClient.cs`
- Implémentation : nom exact de la classe → `ExtendedBet2InvestClient.cs`
- Exception : nom descriptif + `Exception` → `Bet2InvestApiException.cs`
- Tests : nom classe + `Tests` → `ExtendedBet2InvestClientTests.cs`

### Exigences de Tests

**Framework :** xUnit (déjà configuré dans `Bet2InvestPoster.Tests.csproj`)

**Tests existants (11) — 0 RÉGRESSION TOLÉRÉE :**
- `UnitTest1.cs` : 1 test (Worker instantiation)
- `Configuration/OptionsTests.cs` : 10 tests (Options binding, defaults, redaction)

**Nouveaux tests requis — `Services/ExtendedBet2InvestClientTests.cs` :**

Le `Bet2InvestClient` du submodule fait des appels HTTP réels — il n'est PAS mockable facilement (pas d'interface, `HttpClient` interne privé). Les tests doivent donc se concentrer sur la **logique du wrapper** sans appeler l'API réelle.

**Stratégie de test recommandée :**

1. **Tester la logique d'authentification** (sans HTTP réel) :
   - Vérifier que `EnsureAuthenticatedAsync()` appelle login quand `IsAuthenticated == false`
   - Vérifier que le re-login est déclenché quand `_tokenExpiresAt` est passé
   - Vérifier que le re-login n'est PAS déclenché quand le token est encore valide

2. **Tester les exceptions custom** :
   - `Bet2InvestApiException` : vérifier les propriétés (`Endpoint`, `HttpStatusCode`, `ResponsePayload`, `DetectedChange`)
   - `PublishException` : vérifier les propriétés (`BetId`, `HttpStatusCode`, `Message`)
   - Vérifier l'héritage (les deux héritent de `Exception`)

3. **Tester le rate limiting** :
   - Vérifier que deux appels consécutifs respectent le délai configuré (`RequestDelayMs`)
   - Utiliser `Stopwatch` pour mesurer le temps écoulé

4. **Tester la configuration DI** :
   - Vérifier que `ExtendedBet2InvestClient` peut être résolu depuis le `ServiceProvider`
   - Vérifier le lifetime Scoped (deux résolutions dans des scopes différents donnent des instances différentes)

**Pattern de test (aligné avec les tests existants) :**
```csharp
public class ExtendedBet2InvestClientTests
{
    // Pattern : ConfigurationBuilder + InMemoryCollection pour simuler la config
    // Pattern : ServiceCollection pour tester DI registration
    // Pas de mocking framework requis — les tests existants n'en utilisent pas
}
```

**Commandes de validation :**
```bash
dotnet build Bet2InvestPoster.sln
dotnet test tests/Bet2InvestPoster.Tests
# Résultat attendu : tous les anciens tests (11) + nouveaux tests passent, 0 échec
```

**Package de mocking :** Si le dev a besoin de mocker des dépendances, il peut ajouter `Moq` ou `NSubstitute` au projet de tests — mais ce n'est PAS requis. Les tests existants n'utilisent aucun framework de mocking. Privilégier les tests sans mock si possible.

### Intelligence Story Précédente (Epic 1)

**Learnings clés des stories 1.1–1.3 :**

1. **Program.cs est stabilisé** — `AddSystemd()`, Serilog (console + file sinks), 3 Options configurées, fast-fail validation des credentials. Ne modifier que pour ajouter les DI registrations (Task 5).

2. **Pattern Options validé** — `IOptions<Bet2InvestOptions>` avec `SectionName = "Bet2Invest"`, binding via `builder.Configuration.GetSection()`. Le `ToString()` redacte déjà les credentials. Réutiliser ce pattern pour injecter la config dans `ExtendedBet2InvestClient`.

3. **11 tests passent** — 1 test Worker + 10 tests Options. Pattern : `ConfigurationBuilder` avec `AddInMemoryCollection()`, `ServiceCollection` pour DI. Aucun framework de mocking.

4. **Review adversariale story 1.3** — 8 issues corrigés. Les points les plus pertinents pour cette story :
   - Toujours utiliser des chemins absolus dans les configurations système
   - Documenter les choix non-évidents par des commentaires
   - Tester les edge cases (valeurs négatives, chaînes vides)

5. **Submodule OutputType=Exe** — `ProjectReference` standard fonctionne sans conflit. Les modèles du submodule sont directement accessibles via le namespace `JTDev.Bet2InvestScraper`.

### Intelligence Git

**Branche actuelle :** `epic-2/connexion-api` (créée pour l'epic 2)

**Commits récents :**
```
b5f9279 docs(retro): rétrospective épique 1 — fondation du projet terminée
d44dc8e feat(infra): ajouter service systemd et CI GitHub Actions - story 1.3
05a27c3 feat(config): configurer Options, Serilog et injection de dépendances - story 1.2
2aee83c feat(setup): initialiser la solution .NET 9 Worker Service - story 1.1
```

**Pattern de commit :** `type(scope): description en français - story X.Y`
- Types utilisés : `feat`, `docs`
- Pour cette story, le commit devrait être : `feat(api): ExtendedBet2InvestClient authentification et wrapper - story 2.1`

**Fichiers créés dans l'epic 1 :**
- `src/Bet2InvestPoster/` — projet complet avec Program.cs, Worker.cs, Configuration/
- `tests/Bet2InvestPoster.Tests/` — tests Options + Worker
- `deploy/bet2invest-poster.service` — systemd unit
- `.github/workflows/ci.yml` — CI GitHub Actions

### Recherche Technique — Découvertes Critiques sur l'API bet2invest

**L'API bet2invest est privée et non documentée.** Les informations suivantes proviennent d'une rétro-ingénierie du frontend (Nuxt.js/Vue 3) et de tests directs sur `https://api.bet2invest.com`.

#### DÉCOUVERTE 1 : Endpoint des paris à venir (CORRECTION MAJEURE)

**Il n'existe PAS d'endpoint dédié `/bets/upcoming`.** Les paris en attente (pending) sont inclus dans la réponse du endpoint statistiques principal :

- **Endpoint** : `GET /v1/statistics/{userId}` (authentifié — SANS suffixe `/guest`)
- **Réponse** : contient un objet `bets` avec :
  ```json
  {
    "bets": {
      "pending": [...],           // Tableau des paris en attente (VIDE en mode guest)
      "pendingNumber": 30,        // Nombre de paris pending
      "canSeeBets": true/false    // true uniquement si authentifié et autorisé
    }
  }
  ```

**Implication critique :** L'architecture document mentionnait `GET /v1/statistics/{tipsterId}/bets/upcoming` comme endpoint probable — **cet endpoint N'EXISTE PAS**. La méthode `GetUpcomingBetsAsync(int tipsterId)` doit appeler `GET /v1/statistics/{tipsterId}` et extraire `bets.pending` de la réponse.

**Contrainte d'accès :** En mode guest (`/guest`), le tableau `pending` est toujours vide et `canSeeBets = false`. L'authentification est **obligatoire** pour récupérer les paris à venir.

#### DÉCOUVERTE 2 : Endpoint de publication (bet-orders)

**L'endpoint pour publier un pari est `POST /v1/bet-orders`** :

| Méthode | Endpoint | Description |
|---|---|---|
| `POST /v1/bet-orders` | Créer un bet order (publication) |
| `GET /v1/bet-orders` | Lister les orders (query: `bankrollId`) |
| `POST /v1/bet-orders/{id}/cancel` | Annuler un order |

**Body probable de `POST /v1/bet-orders`** (basé sur la structure des bets) :
- `bankrollId` — identifiant du bankroll/compte (probablement récupérable via `/auth/me` ou la réponse statistics)
- `sportId` / `eventId` — sport et événement
- `type` — MONEYLINE, SPREAD, TOTAL_POINTS, etc.
- `team` — TEAM1, TEAM2, null
- `side` — OVER, UNDER, null
- `handicap` — ligne numérique
- `price` — cote décimale
- `units` — mise en unités
- `periodNumber`
- `analysis` — texte optionnel
- `isLive` — boolean

#### DÉCOUVERTE 3 : Champs supplémentaires dans le modèle SettledBet

La réponse API réelle contient **plus de champs** que le modèle `SettledBet` du scraper :
- `private`, `invisible`, `scoresAtBet`
- `specialName`, `contestantLabel`
- `lineId`, `altLineId`, `specialId`, `contestantId`
- `settlementId`, `cancellationCode`
- `clev`, `margin`
- `bankroll` (objet avec `id`, `name`, `type`, `statistics`)
- `market` (objet avec `id`, `key`, `type`, `prices`, `cutoffAt`, `matchupId`)

Le champ `bankroll.id` est probablement nécessaire pour `POST /v1/bet-orders`.

#### DÉCOUVERTE 4 : Endpoints supplémentaires utiles

| Endpoint | Usage pour bet2invest-poster |
|---|---|
| `GET /auth/me` | Récupérer le profil user (et potentiellement le `bankrollId`) |
| `POST /auth/refresh` | Refresh token (body: `{refreshToken}`) — alternative au re-login |
| `GET /bets/authenticated/{betId}` | Détails d'un bet spécifique (authentifié) |
| `POST /v1/tipsters/subscriptions/{id}/free-subscribe` | S'abonner gratuitement à un tipster |

#### SPIKE REQUIS AVANT IMPLÉMENTATION COMPLÈTE

Un **spike avec les vrais credentials** est nécessaire pour confirmer :
1. La structure exacte des objets dans `bets.pending` (probablement similaire à `SettledBet` mais sans les champs de settlement)
2. Le body exact de `POST /v1/bet-orders` (champs requis vs optionnels)
3. Comment obtenir le `bankrollId` du user authentifié
4. Si `LoginResponse` contient un `refreshToken` en plus de `accessToken`

**Recommandation :** Implémenter `GetUpcomingBetsAsync()` et `PublishBetAsync()` avec la meilleure approximation, puis ajuster après le spike. Prévoir des DTOs flexibles avec `[JsonExtensionData]` pour capturer les champs inconnus.

### Références

- [Source: .bmadOutput/planning-artifacts/architecture.md#Core-Architectural-Decisions] — Composition submodule, DI pattern, System.Text.Json
- [Source: .bmadOutput/planning-artifacts/architecture.md#Implementation-Patterns] — Naming, structure, error handling, auth flow
- [Source: .bmadOutput/planning-artifacts/architecture.md#Project-Structure] — Services/, Exceptions/, boundaries
- [Source: .bmadOutput/planning-artifacts/epics.md#Story-2.1] — AC originaux, FR2, FR3, NFR8, NFR9, NFR11
- [Source: .bmadOutput/implementation-artifacts/1-3-deploiement-vps-et-service-systemd.md] — Learnings story 1.3, review adversariale
- [Source: jtdev-bet2invest-scraper/Api/Bet2InvestClient.cs] — API surface du submodule, auth pattern, rate limiting
- [Source: jtdev-bet2invest-scraper/Models/Bet2InvestModels.cs] — Modèles réutilisables (LoginRequest, LoginResponse, Tipster, SettledBet, Pagination)
- [Source: Web research bet2invest.com frontend] — Endpoints non documentés : GET /v1/statistics/{userId} (pending bets), POST /v1/bet-orders (publication)

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

- **DI Bet2InvestClient** : `Bet2InvestClient` (submodule) prend `IConsoleLogger` qui n'est pas directement accessible depuis le projet test. Solution : `SerilogConsoleLoggerAdapter` (internal) + `InternalsVisibleTo("Bet2InvestPoster.Tests")` dans le csproj principal.
- **Auth indépendante** : `ExtendedBet2InvestClient` gère sa propre auth (son propre HttpClient + POST /auth/login) — le token du `Bet2InvestClient` submodule n'est pas partageable (champ privé). Le `Bet2InvestClient` est conservé en Singleton pour usage futur (GetTipstersAsync, GetSettledBetsAsync).
- **Test constructeur interne** : Pattern `internal ExtendedBet2InvestClient(HttpClient, ...)` aligné avec le submodule, activé via `InternalsVisibleTo`.

### Completion Notes List

- ✅ AC#1 : `ExtendedBet2InvestClient` implémente `IExtendedBet2InvestClient` avec `LoginAsync()`, `GetUpcomingBetsAsync(int tipsterId)`, `PublishBetAsync(BetOrderRequest)`, `IsAuthenticated`
- ✅ AC#2 : `EnsureAuthenticatedAsync()` vérifie `IsAuthenticated` (inclut expiration token) — re-login auto si expiré (marge 60s via `expiresIn - 60`)
- ✅ AC#3 : Code isolé dans `Services/ExtendedBet2InvestClient.cs` avec interface `IExtendedBet2InvestClient`, enregistré Scoped en DI
- ✅ AC#4 : `Task.Delay(_options.RequestDelayMs)` avant **chaque** requête API (Login, GetUpcoming, Publish) — validé par tests Stopwatch (≥380ms pour 2×200ms)
- ✅ 27/27 tests passent (11 anciens + 12 story + 4 review), 0 régression
- ✅ Aucun nouveau package NuGet ajouté — System.Text.Json + Serilog.Context + Microsoft.Extensions.Logging déjà disponibles
- ✅ Endpoint découverts utilisés : GET /v1/statistics/{tipsterId} (bets.pending), POST /v1/bet-orders
- ✅ CancellationToken supporté sur toutes les méthodes publiques (graceful shutdown systemd)

### Change Log

| Date | Auteur | Action |
|---|---|---|
| 2026-02-23 | claude-opus-4-6 (create-story) | Création story 2.1 — analyse exhaustive artifacts + recherche API bet2invest |
| 2026-02-23 | claude-sonnet-4-6 (dev-story) | Implémentation complète story 2.1 — 7 fichiers créés, 2 modifiés, 23 tests verts |
| 2026-02-23 | claude-opus-4-6 (code-review) | Review adversariale : 9 issues (2H/4M/3L) corrigées — IsAuthenticated+expiry, PublishBet tests, rate limit login, CancellationToken, LoginAsync→Task, DI test réel, PublishBet retourne orderId, BetOrderRequest→Models/ |

### File List

**Créés :**
- `src/Bet2InvestPoster/Exceptions/Bet2InvestApiException.cs`
- `src/Bet2InvestPoster/Exceptions/PublishException.cs`
- `src/Bet2InvestPoster/Services/IExtendedBet2InvestClient.cs`
- `src/Bet2InvestPoster/Models/BetOrderRequest.cs` (déplacé depuis Services/ lors de la review)
- `src/Bet2InvestPoster/Services/SerilogConsoleLoggerAdapter.cs`
- `src/Bet2InvestPoster/Services/ExtendedBet2InvestClient.cs`
- `tests/Bet2InvestPoster.Tests/Services/ExtendedBet2InvestClientTests.cs`

**Modifiés :**
- `src/Bet2InvestPoster/Program.cs` (ajout DI registrations Singleton Bet2InvestClient + Scoped IExtendedBet2InvestClient)
- `src/Bet2InvestPoster/Bet2InvestPoster.csproj` (ajout InternalsVisibleTo Bet2InvestPoster.Tests)
- `.bmadOutput/implementation-artifacts/sprint-status.yaml` (ready-for-dev → in-progress → review → done)

**Supprimés (review) :**
- `src/Bet2InvestPoster/Services/BetOrderRequest.cs` (déplacé vers Models/)
