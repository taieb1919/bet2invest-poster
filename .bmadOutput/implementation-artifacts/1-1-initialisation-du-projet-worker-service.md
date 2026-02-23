# Story 1.1 : Initialisation du Projet Worker Service

Status: ready-for-dev

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a developpeur,
I want un projet .NET 9 Worker Service initialise avec la structure de solution complete,
So that je dispose de la fondation technique pour developper le service.

## Acceptance Criteria

1. **Given** un repository git avec le submodule `jtdev-bet2invest-scraper`
   **When** le projet est initialise via `dotnet new worker`
   **Then** la solution `Bet2InvestPoster.sln` est creee a la racine du repository
   **And** le projet principal est dans `src/Bet2InvestPoster/`
   **And** le projet de tests est dans `tests/Bet2InvestPoster.Tests/`

2. **Given** le `.csproj` principal (`src/Bet2InvestPoster/Bet2InvestPoster.csproj`)
   **When** les references sont configurees
   **Then** une `ProjectReference` pointe vers `../../jtdev-bet2invest-scraper/JTDev.Bet2InvestScraper.csproj`
   **And** le TargetFramework est `net9.0`
   **And** ImplicitUsings et Nullable sont actives

3. **Given** le `.csproj` principal
   **When** les NuGet packages sont installes
   **Then** les packages suivants sont references avec leurs versions exactes :
   - `Telegram.Bot` 22.9.0
   - `Polly.Core` 8.6.5
   - `Serilog` 4.3.1
   - `Serilog.Extensions.Hosting` 10.0.0
   - `Serilog.Sinks.Console` 6.1.1
   - `Serilog.Sinks.File` 7.0.0
   - `Microsoft.Extensions.Hosting.Systemd` 9.0.8

4. **Given** le projet de tests (`tests/Bet2InvestPoster.Tests/Bet2InvestPoster.Tests.csproj`)
   **When** les packages de test sont configures
   **Then** les packages suivants sont references :
   - `xunit` 2.9.3 (ou `xunit.v3` 3.2.2)
   - `xunit.runner.visualstudio`
   - `Microsoft.NET.Test.Sdk`
   **And** une `ProjectReference` pointe vers le projet principal

5. **Given** la solution complete configuree
   **When** `dotnet build` est execute a la racine
   **Then** le build reussit sans erreur ni warning bloquant

6. **Given** le repository git
   **When** le `.gitignore` est configure
   **Then** les patterns suivants sont exclus : `bin/`, `obj/`, `*.user`, `*.suo`, `appsettings.Development.json`, fichiers de config sensibles

7. **Given** la structure de dossiers du projet principal
   **When** les dossiers sont crees
   **Then** les dossiers suivants existent (vides avec `.gitkeep` si necessaire) :
   - `src/Bet2InvestPoster/Configuration/`
   - `src/Bet2InvestPoster/Models/`
   - `src/Bet2InvestPoster/Services/`
   - `src/Bet2InvestPoster/Telegram/`
   - `src/Bet2InvestPoster/Workers/`
   - `src/Bet2InvestPoster/Exceptions/`

## Tasks / Subtasks

- [ ] Task 1 : Creer la solution et le projet Worker Service (AC: #1)
  - [ ] 1.1 Executer `dotnet new worker -n Bet2InvestPoster --framework net9.0` dans un dossier temporaire
  - [ ] 1.2 Deplacer le contenu genere dans `src/Bet2InvestPoster/`
  - [ ] 1.3 Creer `Bet2InvestPoster.sln` a la racine avec `dotnet new sln`
  - [ ] 1.4 Ajouter le projet principal a la solution : `dotnet sln add src/Bet2InvestPoster/Bet2InvestPoster.csproj`
  - [ ] 1.5 Verifier que `Program.cs` et `Worker.cs` sont bien dans `src/Bet2InvestPoster/`

- [ ] Task 2 : Configurer le .csproj principal et la reference submodule (AC: #2)
  - [ ] 2.1 Verifier `TargetFramework=net9.0`, `ImplicitUsings=enable`, `Nullable=enable`
  - [ ] 2.2 Ajouter `<ProjectReference Include="../../jtdev-bet2invest-scraper/JTDev.Bet2InvestScraper.csproj" />`
  - [ ] 2.3 ATTENTION : le submodule `.csproj` a `OutputType=Exe` — ajouter `<ExcludeAssets>all</ExcludeAssets>` ou utiliser une reference conditionnelle pour eviter les conflits d'entry point. Alternativement, referencer uniquement les fichiers source via `Compile Include`.

- [ ] Task 3 : Installer les NuGet packages (AC: #3)
  - [ ] 3.1 `dotnet add src/Bet2InvestPoster package Telegram.Bot --version 22.9.0`
  - [ ] 3.2 `dotnet add src/Bet2InvestPoster package Polly.Core --version 8.6.5`
  - [ ] 3.3 `dotnet add src/Bet2InvestPoster package Serilog --version 4.3.1`
  - [ ] 3.4 `dotnet add src/Bet2InvestPoster package Serilog.Extensions.Hosting --version 10.0.0`
  - [ ] 3.5 `dotnet add src/Bet2InvestPoster package Serilog.Sinks.Console --version 6.1.1`
  - [ ] 3.6 `dotnet add src/Bet2InvestPoster package Serilog.Sinks.File --version 7.0.0`
  - [ ] 3.7 `dotnet add src/Bet2InvestPoster package Microsoft.Extensions.Hosting.Systemd --version 9.0.8`

- [ ] Task 4 : Creer le projet de tests (AC: #4)
  - [ ] 4.1 `dotnet new xunit -n Bet2InvestPoster.Tests --framework net9.0` dans `tests/`
  - [ ] 4.2 `dotnet sln add tests/Bet2InvestPoster.Tests/Bet2InvestPoster.Tests.csproj`
  - [ ] 4.3 Ajouter `<ProjectReference Include="../../src/Bet2InvestPoster/Bet2InvestPoster.csproj" />`
  - [ ] 4.4 Verifier que les packages xunit, xunit.runner.visualstudio et Microsoft.NET.Test.Sdk sont presents

- [ ] Task 5 : Configurer le .gitignore (AC: #6)
  - [ ] 5.1 Creer/mettre a jour `.gitignore` avec le template .NET standard
  - [ ] 5.2 Ajouter les exclusions specifiques : `appsettings.Development.json`, `*.user`, `*.suo`, `logs/`
  - [ ] 5.3 Conserver les fichiers deja trackes (`README.md`, `.gitmodules`, etc.)

- [ ] Task 6 : Creer la structure de dossiers architecture (AC: #7)
  - [ ] 6.1 Creer `src/Bet2InvestPoster/Configuration/`
  - [ ] 6.2 Creer `src/Bet2InvestPoster/Models/`
  - [ ] 6.3 Creer `src/Bet2InvestPoster/Services/`
  - [ ] 6.4 Creer `src/Bet2InvestPoster/Telegram/`
  - [ ] 6.5 Creer `src/Bet2InvestPoster/Workers/`
  - [ ] 6.6 Creer `src/Bet2InvestPoster/Exceptions/`
  - [ ] 6.7 Ajouter `.gitkeep` dans chaque dossier vide

- [ ] Task 7 : Valider le build complet (AC: #5)
  - [ ] 7.1 Executer `dotnet build` a la racine — zero erreur
  - [ ] 7.2 Executer `dotnet test` — les tests par defaut passent
  - [ ] 7.3 Verifier que la reference au submodule scraper compile correctement

## Dev Notes

### Project Structure Notes

### References

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List
