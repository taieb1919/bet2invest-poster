# Story 10.2: Rotation des Logs et Rétention Configurable

Status: review

## Story

As a l'utilisateur,
I want que les logs soient automatiquement rotés et purgés selon une durée configurable,
so that l'espace disque du VPS ne soit pas saturé par les fichiers de logs.

## Acceptance Criteria

1. **Given** Serilog configuré avec le sink File **When** le service écrit des logs **Then** les fichiers de logs sont rotés quotidiennement (NFR13) **And** le nom du fichier inclut la date (ex: `bet2invest-poster-20260224.log`)
2. **Given** `PosterOptions.LogRetentionDays` configuré (ex: 30) **When** un nouveau fichier de log est créé **Then** les fichiers de log plus anciens que `LogRetentionDays` jours sont supprimés automatiquement
3. **Given** `LogRetentionDays` non configuré **When** le service démarre **Then** la rétention par défaut est de 30 jours

## Tasks / Subtasks

- [x] Task 1 : Ajouter `LogRetentionDays` à `PosterOptions` (AC: #2, #3)
  - [x] 1.1 Ajouter propriété `public int LogRetentionDays { get; set; } = 30;` dans `PosterOptions.cs`
  - [x] 1.2 Ajouter `"LogRetentionDays": 30` dans `appsettings.json` section Poster
- [x] Task 2 : Configurer `retainedFileCountLimit` dans Program.cs (AC: #1, #2, #3)
  - [x] 2.1 Lire `LogRetentionDays` depuis la configuration avant le setup Serilog (même pattern que `LogPath`)
  - [x] 2.2 Passer `retainedFileCountLimit: logRetentionDays` au sink File existant
  - [x] 2.3 Vérifier que le rolling file produit déjà le format `bet2invest-poster-YYYYMMDD.log` (déjà le cas avec `RollingInterval.Day` et le suffixe `-` dans le path actuel)
- [x] Task 3 : Tests unitaires (AC: #1, #2, #3)
  - [x] 3.1 Test : `PosterOptions.LogRetentionDays` a une valeur par défaut de 30
  - [x] 3.2 Test : `LogRetentionDays` est bindable depuis la configuration (optionnel — la config binding est couverte par le framework)

## Dev Notes

### Contexte — Ce qui existe déjà

La rotation quotidienne est **déjà en place**. Dans `Program.cs:35-38` :

```csharp
.WriteTo.File(
    path: Path.Combine(logPath, "bet2invest-poster-.log"),
    rollingInterval: RollingInterval.Day,
    outputTemplate: fileTemplate));
```

Serilog.Sinks.File 7.0 avec `RollingInterval.Day` produit déjà des fichiers nommés `bet2invest-poster-20260225.log`. L'AC #1 est **déjà satisfait** par le code actuel.

Ce qui manque : le paramètre `retainedFileCountLimit` n'est pas configuré. Serilog.Sinks.File 7.0 a un défaut de **31 fichiers**, ce qui correspond approximativement à 30 jours. Cependant, l'AC #2 exige que ce soit **configurable** via `PosterOptions.LogRetentionDays`.

### Implémentation — Changement minimal

Le changement se résume à :
1. Ajouter `LogRetentionDays` dans `PosterOptions` (1 ligne)
2. Lire la valeur depuis la config dans `Program.cs` (1 ligne)
3. Passer `retainedFileCountLimit` au sink File (1 paramètre supplémentaire)

```csharp
// Program.cs — avant setup Serilog (même pattern que logPath ligne 19)
var logRetentionDays = builder.Configuration.GetValue<int?>("Poster:LogRetentionDays") ?? 30;

// Dans le .WriteTo.File(...)
.WriteTo.File(
    path: Path.Combine(logPath, "bet2invest-poster-.log"),
    rollingInterval: RollingInterval.Day,
    retainedFileCountLimit: logRetentionDays,
    outputTemplate: fileTemplate));
```

### IMPORTANT — `retainedFileCountLimit` compte les fichiers, pas les jours

Serilog.Sinks.File `retainedFileCountLimit` est un **nombre de fichiers** conservés, pas un nombre de jours. Avec `RollingInterval.Day`, 1 fichier = 1 jour, donc `retainedFileCountLimit: 30` = 30 jours de rétention. La correspondance est directe.

### Configuration via variables d'environnement

La surcharge fonctionne automatiquement via Generic Host : `Poster__LogRetentionDays=60` override `appsettings.json`.

### Fichiers à modifier

| Fichier | Modification |
|---------|-------------|
| `src/Bet2InvestPoster/Configuration/PosterOptions.cs` | Ajouter `LogRetentionDays` (1 ligne) |
| `src/Bet2InvestPoster/Program.cs` | Lire `LogRetentionDays` + passer à `retainedFileCountLimit` |
| `src/Bet2InvestPoster/appsettings.json` | Ajouter `"LogRetentionDays": 30` |

### Fichiers à créer

Aucun fichier à créer. Pas de nouveau service, pas de nouvelle classe.

### Project Structure Notes

- Modification mineure dans 3 fichiers existants uniquement
- Aucun changement architectural — simple ajout de paramètre de configuration
- Pattern identique à `LogPath` (lecture anticipée avant setup Serilog)

### Testing Standards

- La valeur par défaut de `PosterOptions.LogRetentionDays` peut être testée via un test simple d'instanciation
- Le comportement de rétention de Serilog lui-même n'a PAS besoin d'être testé (responsabilité du framework Serilog.Sinks.File)
- Les 236+ tests existants ne doivent pas être impactés (aucune interface modifiée)

### Learnings Story 10.1

1. Ne pas sur-ingénier : si le framework fournit déjà la fonctionnalité, il suffit de la configurer
2. Pattern de lecture anticipée de config dans `Program.cs` (avant setup Serilog) déjà établi avec `LogPath`
3. 236+ tests passent — pas de régression attendue car aucune interface n'est modifiée

### References

- [Source: .bmadOutput/planning-artifacts/epics-phase2.md#Story 10.2]
- [Source: src/Bet2InvestPoster/Program.cs#L35-38 — configuration Serilog actuelle]
- [Source: src/Bet2InvestPoster/Configuration/PosterOptions.cs — options existantes]
- [Source: src/Bet2InvestPoster/appsettings.json — configuration JSON]
- [Source: https://github.com/serilog/serilog-sinks-file — Serilog.Sinks.File 7.0 documentation]
- [Source: .bmadOutput/implementation-artifacts/10-1-onboarding-guide-telegram.md — learnings story précédente]

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

Aucun blocage rencontré. Implémentation directe conformément aux Dev Notes.

### Completion Notes List

- Task 1 : Propriété `LogRetentionDays` (défaut 30) ajoutée dans `PosterOptions.cs` + `appsettings.json`
- Task 2 : `logRetentionDays` lu depuis `Poster:LogRetentionDays` avant setup Serilog (pattern identique à `LogPath`) ; `retainedFileCountLimit` passé au sink File
- Task 3 : 2 nouveaux tests dans `OptionsTests.cs` — valeur par défaut 30 + binding depuis configuration ; total 246 tests passent (0 régression)
- AC #1 déjà satisfait (RollingInterval.Day existant) ; AC #2 et #3 satisfaits par la nouvelle configuration

### File List

- src/Bet2InvestPoster/Configuration/PosterOptions.cs
- src/Bet2InvestPoster/Program.cs
- src/Bet2InvestPoster/appsettings.json
- tests/Bet2InvestPoster.Tests/Configuration/OptionsTests.cs

### Change Log

- 2026-02-25 : Implémentation story 10.2 — ajout `LogRetentionDays` configurable (défaut 30 jours) dans PosterOptions + retainedFileCountLimit dans Serilog sink File
- 2026-02-25 : Code review adversarial — 7 issues trouvées (2 HIGH, 3 MEDIUM, 2 LOW). Fixes appliqués : validation LogRetentionDays > 0 dans Program.cs, ajout LogRetentionDays au test PosterOptions_BindsCorrectlyFromConfiguration, suppression test dupliqué PosterOptions_LogRetentionDays_DefaultIs30. 245 tests passent.
