# Csproj Migrator — Spécification technique

## 1. Objectif

`csproj-migrator` est un CLI .NET/C# permettant d'automatiser la migration de projets .NET depuis une architecture legacy vers une architecture multi-target, en utilisant un dépôt de référence moderne.

Le programme travaille avec trois dépôts locaux :

- **Repository A — Source / Legacy**
  - source de vérité pour le code et la configuration existante ;
  - ne doit jamais être modifié.

- **Repository B — Reference / Modern**
  - contient des implémentations modernes et/ou des variantes de projets ;
  - sert de référence pour les Target Frameworks, le code et les dépendances modernes ;
  - ne doit jamais être modifié.

- **Repository C — Destination**
  - contient le résultat de la migration ;
  - c'est le seul dépôt qui peut être modifié.

Principe fondamental :

> A définit ce qui doit être conservé.
> B définit les variantes modernes à intégrer.
> C reçoit le résultat de la migration.

---

# 2. Objectifs fonctionnels

Le CLI doit être capable de :

1. découvrir automatiquement les projets dans A et B ;
2. identifier les projets logiquement équivalents même lorsque leurs noms physiques diffèrent ;
3. associer plusieurs projets B à un même projet A ;
4. analyser les Target Frameworks ;
5. calculer automatiquement les Target Frameworks du projet C ;
6. convertir les projets legacy vers SDK-style lorsque nécessaire ;
7. analyser et préserver les versions NuGet legacy ;
8. utiliser les versions modernes de B pour les nouveaux TFMs lorsque cela est possible ;
9. analyser les dépendances transitives ;
10. détecter les incompatibilités par TFM ;
11. analyser le code C# avec Roslyn ;
12. incorporer les variantes de code provenant de B ;
13. générer des conditions `#if` lorsque nécessaire ;
14. modifier uniquement C ;
15. créer une branche Git dédiée ;
16. proposer les décisions ambiguës à l'utilisateur via un CLI interactif ;
17. permettre un mode `--dry-run` ;
18. permettre un mode `--non-interactive` pour CI/CD ;
19. restaurer, compiler et tester chaque TFM ;
20. produire un rapport complet de migration.

---

# 3. Non-objectifs

Le programme ne doit pas :

- modifier A ;
- modifier B ;
- supprimer automatiquement des projets présents uniquement dans A ;
- ajouter automatiquement tous les projets présents uniquement dans B ;
- mettre à jour automatiquement les packages legacy ;
- remplacer silencieusement un package legacy par un autre ;
- choisir arbitrairement entre deux mappings ambigus ;
- prétendre qu'un TFM est compatible sans analyse ;
- faire des remplacements textuels aveugles dans le code C#.

En cas d'incertitude :

> analyser → expliquer → demander une décision → appliquer.

---

# 4. Exemple de commande

## Mode interactif

```bash
csproj-migrator migrate \
  --source ../repo-a \
  --reference ../repo-b \
  --destination ../repo-c
```

Le CLI demande ensuite les décisions nécessaires.

## Dry-run

```bash
csproj-migrator migrate \
  --source ../repo-a \
  --reference ../repo-b \
  --destination ../repo-c \
  --dry-run
```

Aucune modification ne doit être effectuée.

## Branche explicite

```bash
csproj-migrator migrate \
  --source ../repo-a \
  --reference ../repo-b \
  --destination ../repo-c \
  --branch feature/multitarget
```

## Mode CI

```bash
csproj-migrator migrate \
  --source ../repo-a \
  --reference ../repo-b \
  --destination ../repo-c \
  --branch feature/multitarget \
  --non-interactive \
  --config migration.yml
```

---

# 5. Commandes du CLI

Le CLI expose quatre commandes principales.

## `analyze`

Analyse les repositories sans modification.

```bash
csproj-migrator analyze \
  --source ../repo-a \
  --reference ../repo-b \
  --destination ../repo-c
```

Produit :

- projets détectés ;
- mappings ;
- TFMs ;
- dépendances ;
- packages ;
- conflits ;
- incompatibilités ;
- ambiguïtés.

---

## `plan`

Construit un plan de migration.

```bash
csproj-migrator plan \
  --source ../repo-a \
  --reference ../repo-b \
  --destination ../repo-c
```

Produit par exemple :

```text
migration-plan.json
```

ou :

```text
migration.yml
```

Le plan peut ensuite être relu et utilisé sans refaire les choix interactifs.

---

## `migrate`

Applique le plan.

```bash
csproj-migrator migrate \
  --source ../repo-a \
  --reference ../repo-b \
  --destination ../repo-c
```

---

## `validate`

Valide le résultat.

```bash
csproj-migrator validate \
  --destination ../repo-c
```

La validation doit notamment effectuer :

```text
restore
build
test
```

pour chaque TFM applicable.

---

# 6. CLI interactif

Le CLI doit être interactif par défaut lorsque des décisions sont nécessaires.

Exemple :

```text
CSProj Migrator
────────────────────────────────────────

Source       : ../repo-a
Reference    : ../repo-b
Destination  : ../repo-c

✓ Git repositories detected
✓ 14 source projects found
✓ 18 reference projects found
✓ 14 logical projects detected
✓ 13 projects matched automatically

⚠ 1 ambiguous project mapping

Project: Core
```

Le CLI affiche les candidats :

```text
Reference candidates:

  1. Core.csproj
     TFMs: net8.0;netstandard2.0;netcoreapp3.1

  2. Core.Net8.csproj
     TFM: net8.0

  3. Core.Standard.csproj
     TFM: netstandard2.0

Select:
> 1
  2
  3
  Manual mapping
  Skip
```

Le CLI doit toujours expliquer pourquoi une décision est demandée.

---

# 7. Mode non interactif

Le mode :

```bash
--non-interactive
```

est destiné à la CI/CD.

Il ne doit jamais choisir arbitrairement une option ambiguë.

Exemple :

```text
ERROR MIGRATION_AMBIGUOUS_MAPPING

Project:
  Core

Candidates:
  Core.Net8.csproj
  Core.Standard.csproj

No deterministic mapping found.

Resolution:
  Add an explicit mapping to migration.yml.
```

Le processus retourne un exit code non nul.

---

# 8. Les trois repositories

## Repository A

Repository legacy.

Contraintes :

- lecture seule ;
- aucune modification Git ;
- aucune modification de fichier ;
- aucune installation de package ;
- aucune génération de fichier.

---

## Repository B

Repository de référence.

Contraintes :

- lecture seule ;
- utilisé pour identifier les variantes modernes ;
- utilisé comme source de code de référence ;
- utilisé pour les TFMs ;
- utilisé pour les packages modernes.

---

## Repository C

Repository destination.

Seul repository modifiable.

Avant migration :

```bash
git -C ../repo-c status
```

Le programme doit vérifier l'état du repository.

Par défaut, si C contient des modifications non commitées susceptibles d'être écrasées, la migration doit être refusée.

---

# 9. Gestion de la branche Git

La migration doit créer une branche dédiée.

Par défaut :

```text
feature/multitarget
```

Commande conceptuelle :

```bash
git -C ../repo-c checkout -b feature/multitarget
```

A et B ne doivent subir aucune opération Git de modification.

Le CLI doit afficher :

```text
Creating branch:
  feature/multitarget

✓ Branch created
```

Si la branche existe déjà :

```text
⚠ Branch already exists

Options:
  1. Abort
  2. Checkout existing branch
  3. Use another branch
```

Aucune décision silencieuse.

---

# 10. Découverte des projets

Le CLI recherche les fichiers :

```text
**/*.csproj
```

dans A et B.

Pour chaque projet, il collecte :

- chemin ;
- nom physique ;
- nom logique ;
- `ProjectGuid` ;
- `AssemblyName` ;
- `PackageId` ;
- `RootNamespace` ;
- `TargetFramework` ;
- `TargetFrameworks` ;
- `ProjectReference` ;
- `PackageReference` ;
- `Reference` ;
- `Compile` ;
- `EmbeddedResource` ;
- `Content` ;
- `None` ;
- imports ;
- targets custom ;
- propriétés MSBuild.

---

# 11. Identité logique d'un projet

Le nom physique du `.csproj` ne constitue pas l'identité du projet.

Exemple :

```text
Core.csproj
Core.Net8.csproj
Core.Standard.csproj
Core.NetCore.csproj
```

peuvent représenter le même projet logique :

```text
Core
```

Le système introduit donc :

```text
LogicalProjectId
```

---

# 12. Normalisation du nom

La normalisation doit supprimer les suffixes de variantes framework connus.

Exemples :

```text
Core
Core.Net8
Core.Standard
Core.NetStandard
Core.NetCore
Core.NetCore31
Core.NetFramework
Core.Framework
Core.Legacy
```

deviennent :

```text
Core
```

La liste des suffixes doit être configurable.

Exemple :

```yaml
projectMatching:
  frameworkSuffixes:
    - Net8
    - Net7
    - Net6
    - Standard
    - NetStandard
    - NetCore
    - NetCore31
    - NetFramework
    - Framework
    - Legacy
```

La normalisation ne doit pas supprimer arbitrairement une partie du nom.

---

# 13. Stratégie de matching A ↔ B

Ordre de priorité :

1. mapping explicite ;
2. nom logique normalisé ;
3. `ProjectGuid` ;
4. `AssemblyName` ;
5. `PackageId` ;
6. `RootNamespace` ;
7. chemin ;
8. graphe de dépendances ;
9. autres métadonnées MSBuild.

Le système doit conserver la raison du mapping.

Exemple :

```text
Core.csproj
    ↓
Core.Net8.csproj

Match reason:
  LogicalName = Core
```

---

# 14. Plusieurs projets B pour un projet A

C'est un cas normal.

Exemple :

```text
A:
  Core.csproj
    net472

B:
  Core.Net8.csproj
    net8.0

  Core.Standard.csproj
    netstandard2.0
```

Le mapping devient :

```text
A.Core
 ├── B.Core.Net8
 └── B.Core.Standard
```

Les projets B ne doivent pas être considérés comme deux projets logiques différents.

---

# 15. Mapping explicite

Lorsque le matching automatique n'est pas suffisant, le fichier de configuration peut contenir :

```yaml
projects:
  - source: src/Core/Core.csproj
    references:
      - project: src/Core/Core.Standard.csproj
        frameworks:
          - netstandard2.0

      - project: src/Core/Core.Net8.csproj
        frameworks:
          - net8.0
```

Le mapping explicite est prioritaire sur tous les heuristiques.

---

# 16. Ambiguïté

Une ambiguïté existe lorsque plusieurs candidats peuvent représenter le même projet et qu'aucune règle déterministe ne permet de choisir.

Exemple :

```text
Core.Net8.csproj
Core.Net8.Legacy.csproj
```

si les deux exposent :

```text
net8.0
```

et aucun autre critère ne permet de les distinguer.

Le CLI doit alors demander :

```text
⚠ Ambiguous mapping

Which project should be used?

1. Core.Net8.csproj
2. Core.Net8.Legacy.csproj
3. Define explicit mapping
4. Abort
```

---

# 17. Target Frameworks

Le TFM est calculé automatiquement.

Il n'existe **pas** de paramètre :

```text
--target-framework
```

Le résultat est :

```text
TFM(C) =
    Unique(
        TFM(A)
        +
        TFM(all matching B projects)
    )
```

---

# 18. Exemple de calcul

A :

```xml
<TargetFramework>net472</TargetFramework>
```

B :

```xml
<TargetFrameworks>
  net8.0;netstandard2.0;netcoreapp3.1
</TargetFrameworks>
```

C devient :

```xml
<TargetFrameworks>
  net472;net8.0;netstandard2.0;netcoreapp3.1
</TargetFrameworks>
```

---

# 19. Variantes multiples dans B

A :

```text
Core.csproj
net472
```

B :

```text
Core.Net8.csproj
net8.0

Core.Standard.csproj
netstandard2.0
```

C :

```xml
<TargetFrameworks>
  net472;net8.0;netstandard2.0
</TargetFrameworks>
```

---

# 20. Déduplication

Les TFMs doivent être normalisés avant comparaison.

Exemple :

```text
net8.0
NET8.0
```

doivent représenter le même framework.

Le résultat doit être dédupliqué et présenté dans un ordre déterministe.

---

# 21. Ne pas importer les TFMs non liés

Si B contient :

```text
Core → net8.0;netstandard2.0
OtherProject → net9.0
```

`net9.0` ne doit pas être ajouté à `Core`.

Seuls les projets B associés au projet logique concerné sont pris en compte.

---

# 22. Analyse du graphe de dépendances

Le CLI doit construire un graphe de projets.

Exemple :

```text
Core
 ├── Data
 │    └── Database
 └── Common
```

L'analyse doit être récursive.

Elle doit permettre de déterminer si chaque projet peut réellement supporter les TFMs calculés.

---

# 23. Compatibilité des ProjectReference

La compatibilité doit être analysée **par TFM**.

Exemple :

```text
Core
  net472
  netstandard2.0
```

mais :

```text
Data
  net472
```

Alors :

```text
Core/net472 → Data/net472       ✓
Core/netstandard2.0 → Data      ✗
```

Le CLI doit signaler :

```text
BLOCKED

Core
└── Data

Target:
  netstandard2.0

Data does not support netstandard2.0.
```

---

# 24. Chemin complet des erreurs

Le rapport doit montrer le chemin de dépendance.

Exemple :

```text
Core
└── Data
    └── Legacy.Database
        └── net472 only
```

Cela permet de comprendre immédiatement pourquoi un TFM est bloqué.

---

# 25. Projet présent uniquement dans A

Un projet uniquement présent dans A est conservé.

Exemple :

```text
A:
  LegacyReporting.csproj

B:
  absent
```

Le projet ne doit pas être supprimé.

Le CLI indique :

```text
INFO

LegacyReporting.csproj exists only in source.

Action:
  Keep unchanged unless required by migration.
```

---

# 26. Projet présent uniquement dans B

Un projet uniquement présent dans B n'est pas ajouté automatiquement.

Exemple :

```text
B:
  NewModernFeature.csproj
```

Le CLI indique :

```text
INFO

NewModernFeature.csproj exists only in reference.

Action:
  Not automatically imported.
```

Il peut néanmoins être utilisé comme référence lors de l'analyse ou du matching.

---

# 27. Conversion SDK-style

Les projets legacy qui doivent devenir multi-target doivent être convertis en SDK-style.

Exemple legacy :

```xml
<Project ToolsVersion="15.0">
  <PropertyGroup>
    <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="Class1.cs" />
  </ItemGroup>
</Project>
```

devient :

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>
      net472;netstandard2.0
    </TargetFrameworks>
  </PropertyGroup>
</Project>
```

---

# 28. Analyse préalable à la conversion SDK

Avant conversion, le CLI doit analyser :

- `Reference`
- `HintPath`
- `Compile`
- `EmbeddedResource`
- `Content`
- `None`
- `Import`
- `BeforeBuild`
- `AfterBuild`
- `Target`
- propriétés personnalisées
- `AssemblyInfo`
- `packages.config`
- `App.config`

Une construction complexe ne doit jamais être supprimée silencieusement.

---

# 29. Compile Include

En SDK-style, les fichiers `.cs` sont implicitement inclus.

Un :

```xml
<Compile Include="Class1.cs" />
```

peut donc être supprimé.

Mais avant suppression, le CLI doit vérifier :

- `Link`
- `DependentUpon`
- `Visible`
- `Generator`
- `AutoGen`
- autres métadonnées particulières.

Si une sémantique particulière existe :

```text
REVIEW
```

---

# 30. Packages NuGet

Le système doit utiliser les packages de A comme source de vérité pour les TFMs legacy.

Principe :

> Une migration multi-target ne doit jamais transformer automatiquement une mise à niveau de framework en mise à niveau de package legacy.

---

# 31. Central Package Management

Les versions doivent être centralisées dans :

```text
Directory.Packages.props
```

Exemple :

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>

  <ItemGroup>
    <PackageVersion
      Include="Newtonsoft.Json"
      Version="12.0.3" />
  </ItemGroup>
</Project>
```

Le `.csproj` contient alors :

```xml
<PackageReference Include="Newtonsoft.Json" />
```

---

# 32. Versions différentes selon le TFM

Si le legacy utilise :

```text
net472 → Newtonsoft.Json 12.0.3
```

et la version moderne :

```text
net8.0 → Newtonsoft.Json 13.0.3
```

le CLI génère :

```xml
<PackageVersion
    Include="Newtonsoft.Json"
    Version="12.0.3"
    Condition="'$(TargetFramework)' == 'net472'" />

<PackageVersion
    Include="Newtonsoft.Json"
    Version="13.0.3"
    Condition="'$(TargetFramework)' == 'net8.0'" />
```

---

# 33. Version identique

Si tous les TFMs utilisent la même version :

```xml
<PackageVersion
    Include="Newtonsoft.Json"
    Version="13.0.3" />
```

Aucune condition inutile ne doit être générée.

---

# 34. Package legacy incompatible

Si un package ne fonctionne que sur :

```text
net472
```

le CLI ne doit pas automatiquement le remplacer.

Résultat :

```text
BLOCKED

Package:
  Legacy.Database.Client

Supported:
  net472

Required:
  netstandard2.0

No safe automatic replacement found.
```

Une alternative détectée dans B peut être affichée comme :

```text
Potential alternative:
  Modern.Database.Client
```

mais ne doit pas être installée automatiquement.

---

# 35. Analyse du code

L'analyse C# doit utiliser **Roslyn**.

Le système doit pouvoir analyser :

- syntax tree ;
- semantic model ;
- symboles ;
- références ;
- types ;
- namespaces ;
- appels API ;
- préprocesseur ;
- dépendances d'assemblies.

---

# 36. Code spécifique à un framework

Le CLI doit détecter les différences de code entre les variantes B.

Exemple :

```text
Core.Net8.csproj
Core.Standard.csproj
```

peuvent fournir deux implémentations différentes.

Le système doit déterminer si ces différences doivent être :

1. fusionnées ;
2. conditionnées par TFM ;
3. représentées par des fichiers différents ;
4. soumises à revue manuelle.

---

# 37. Symboles de compilation

Le CLI doit utiliser les symboles standards correspondant aux TFMs.

Exemples :

```text
net472
→ NET472

net8.0
→ NET8_0

netstandard2.0
→ NETSTANDARD2_0

netcoreapp3.1
→ NETCOREAPP3_1
```

Exemple :

```csharp
#if NET472

LegacyImplementation();

#elif NETSTANDARD2_0

StandardImplementation();

#elif NET8_0

ModernImplementation();

#endif
```

---

# 38. Priorité au `.csproj` sur `#if`

Lorsque la différence concerne des fichiers entiers, il est préférable d'utiliser les conditions MSBuild.

Exemple :

```xml
<ItemGroup Condition="'$(TargetFramework)' == 'net8.0'">
  <Compile Include="Modern\**\*.cs" />
</ItemGroup>
```

plutôt que d'injecter des `#if` partout.

Les `#if` sont réservés aux différences réellement intra-fichier.

---

# 39. Classification des changements de code

Chaque changement doit être classifié.

## SAFE

Transformation déterministe.

Exemple :

```text
Ajouter un using inutilisé détecté par Roslyn
```

ou :

```text
Ajouter un fichier explicitement identifié comme variante net8.0
```

## REVIEW

Transformation plausible mais nécessitant une validation humaine.

Exemple :

```text
Même type logique mais implémentation différente entre A et B.
```

## BLOCKED

Impossible de transformer automatiquement de manière sûre.

Exemple :

```text
API legacy sans équivalent déterministe.
```

---

# 40. Ne jamais faire de remplacement textuel aveugle

Le programme ne doit pas effectuer de logique du type :

```text
replace("OldApi", "NewApi")
```

sans analyse syntaxique/sémantique.

Toute transformation C# doit être effectuée avec Roslyn ou être explicitement classifiée comme opération textuelle sûre.

---

# 41. Plan de migration

Avant toute modification, le CLI construit un objet :

```text
MigrationPlan
```

contenant notamment :

```text
SourceRepository
ReferenceRepository
DestinationRepository
Branch
Projects
Mappings
TargetFrameworks
Packages
Dependencies
CodeChanges
Warnings
Blockers
```

---

# 42. Exemple de plan

```text
PROJECT MATCHING
────────────────────────────────

A.Core.csproj

Logical name:
  Core

Reference projects:

  Core.csproj
    net8.0
    netstandard2.0
    netcoreapp3.1

  Core.Net8.csproj
    net8.0

Destination TFMs:

  net472
  net8.0
  netstandard2.0
  netcoreapp3.1
```

---

# 43. Aperçu avant migration

Le CLI doit afficher un résumé avant toute écriture.

Exemple :

```text
Migration summary
────────────────────────────────

Projects:
  14

To convert to SDK-style:
  9

Multi-target projects:
  11

New TFMs:
  net8.0
  netstandard2.0
  netcoreapp3.1

Packages preserved:
  37

Package version conflicts:
  4

Code changes:
  SAFE     26
  REVIEW    7
  BLOCKED   1

Proceed with migration? [y/N]
```

Si un `BLOCKED` critique existe, le comportement par défaut est d'empêcher la migration.

---

# 44. Exécution de la migration

Ordre d'exécution :

1. valider A ;
2. valider B ;
3. valider C ;
4. analyser Git ;
5. découvrir les projets ;
6. parser les projets ;
7. matcher A ↔ B ;
8. construire les graphes ;
9. analyser les packages ;
10. calculer les TFMs ;
11. générer le plan ;
12. demander les décisions interactives ;
13. créer la branche C ;
14. convertir les `.csproj` ;
15. mettre à jour les packages ;
16. appliquer les changements de code ;
17. restaurer ;
18. compiler ;
19. tester ;
20. générer le rapport.

---

# 45. Validation

Pour chaque projet et chaque TFM :

```text
dotnet restore
dotnet build
dotnet test
```

ou l'équivalent approprié pour les projets legacy.

Le rapport doit distinguer :

```text
RESTORE
BUILD
TEST
```

---

# 46. Compilation par TFM

Exemple :

```text
Core
────────────────────────────────

net472
  Restore    ✓
  Build      ✓
  Tests      ✓

netstandard2.0
  Restore    ✓
  Build      ✗

  Error:
    Legacy.Database reference is incompatible

net8.0
  Restore    ✓
  Build      ✓
  Tests      ✓
```

---

# 47. Rollback

Le programme doit pouvoir restaurer C à l'état initial en cas d'échec.

Le mécanisme privilégié est Git.

Avant modification :

```text
initial commit / HEAD
```

En cas d'échec critique :

```text
Migration failed.

Destination repository was restored to:
  <commit>
```

La branche de migration peut être conservée pour inspection ou supprimée après confirmation.

---

# 48. Rapport final

Le rapport doit contenir :

- repositories ;
- branche ;
- projets analysés ;
- mappings ;
- projets A-only ;
- projets B-only ;
- TFMs avant/après ;
- conversions SDK ;
- packages ;
- versions conservées ;
- versions conditionnelles ;
- dépendances ;
- changements de code ;
- warnings ;
- blockers ;
- restore ;
- build ;
- tests.

---

# 49. Exemple de résultat final

```text
CSProj Migrator
══════════════════════════════════════

Migration completed.

Destination:
  ../repo-c

Branch:
  feature/multitarget

Projects:
  14 migrated
  0 deleted
  2 kept unchanged

Target frameworks:
  net472
  netstandard2.0
  netcoreapp3.1
  net8.0

SDK migrations:
  9

Packages:
  37 preserved
  8 conditional versions
  0 legacy upgrades

Code changes:
  SAFE      26
  REVIEW     7
  BLOCKED    0

Validation:
  Restore    ✓
  Build      ✓
  Tests      ✓

Report:
  migration-report.json
```

---

# 50. Architecture

```text
CsprojMigrator
│
├── CLI
│   ├── AnalyzeCommand
│   ├── PlanCommand
│   ├── MigrateCommand
│   └── ValidateCommand
│
├── Repository
│   └── GitRepository
│
├── Discovery
│   ├── ProjectDiscovery
│   ├── LogicalProjectNormalizer
│   └── ProjectMatcher
│
├── Frameworks
│   ├── TargetFrameworkParser
│   └── TargetFrameworkMerger
│
├── Dependencies
│   ├── ProjectGraphBuilder
│   ├── PackageAnalyzer
│   └── CompatibilityAnalyzer
│
├── Projects
│   ├── CsprojReader
│   ├── SdkConverter
│   └── CsprojWriter
│
├── Packages
│   ├── CentralPackageManager
│   └── PackageConditionGenerator
│
├── Code
│   ├── RoslynAnalyzer
│   ├── CodeMatcher
│   ├── CodeMergeEngine
│   └── ConditionalCompilationGenerator
│
├── Migration
│   ├── MigrationPlan
│   ├── MigrationPlanner
│   └── MigrationExecutor
│
└── Validation
    ├── RestoreRunner
    ├── BuildRunner
    └── TestRunner
```

---

# 51. Modèle objet principal

Exemple conceptuel :

```csharp
public sealed class ProjectInfo
{
    public string Path { get; init; }
    public string PhysicalName { get; init; }
    public string LogicalName { get; init; }

    public IReadOnlyList<TargetFramework> TargetFrameworks { get; init; }

    public IReadOnlyList<ProjectReferenceInfo> ProjectReferences { get; init; }

    public IReadOnlyList<PackageReferenceInfo> PackageReferences { get; init; }
}
```

Mapping :

```csharp
public sealed class ProjectMapping
{
    public ProjectInfo Source { get; init; }

    public IReadOnlyList<ProjectInfo> References { get; init; }

    public MappingReason Reason { get; init; }

    public bool IsAmbiguous { get; init; }
}
```

Plan :

```csharp
public sealed class MigrationPlan
{
    public IReadOnlyList<ProjectMigration> Projects { get; init; }

    public IReadOnlyList<PackageChange> Packages { get; init; }

    public IReadOnlyList<CodeChange> CodeChanges { get; init; }

    public IReadOnlyList<MigrationIssue> Issues { get; init; }
}
```

---

# 52. Configuration

Le fichier de configuration peut contenir :

```yaml
projectMatching:
  frameworkSuffixes:
    - Net8
    - Standard
    - NetStandard
    - NetCore
    - NetCore31
    - Legacy

projects:
  - source: src/Core/Core.csproj
    references:
      - project: src/Core/Core.Net8.csproj
        frameworks:
          - net8.0

      - project: src/Core/Core.Standard.csproj
        frameworks:
          - netstandard2.0

packages:
  preserveLegacyVersions: true

migration:
  failOnBlocked: true
  failOnAmbiguous: true

validation:
  restore: true
  build: true
  test: true
```

---

# 53. Principes de sécurité

Les règles suivantes sont obligatoires :

### Règle 1

Ne jamais modifier A.

### Règle 2

Ne jamais modifier B.

### Règle 3

C est le seul repository modifiable.

### Règle 4

Ne jamais supprimer automatiquement un projet A-only.

### Règle 5

Ne jamais ajouter automatiquement tous les projets B-only.

### Règle 6

Ne jamais mettre à jour automatiquement un package legacy.

### Règle 7

Ne jamais remplacer silencieusement un package.

### Règle 8

Ne jamais choisir silencieusement un mapping ambigu.

### Règle 9

Ne jamais supprimer du code sans analyse.

### Règle 10

Ne jamais déclarer un TFM compatible sans analyser les dépendances.

### Règle 11

Toute erreur de compilation est une erreur réelle, pas un warning ignoré.

### Règle 12

En cas d'incertitude :

```text
ANALYZE → REPORT → REVIEW
```

---

# 54. Principes d'architecture

Le système doit respecter les principes suivants :

- les décisions métier doivent être séparées de l'I/O ;
- l'analyse doit être exécutable sans modifier les repositories ;
- le plan doit être déterministe ;
- l'exécution doit appliquer uniquement un plan validé ;
- les transformations doivent être idempotentes autant que possible ;
- les mappings doivent être traçables ;
- chaque décision automatique doit avoir une raison explicable ;
- les transformations irréversibles doivent être évitées ou protégées par Git.

---

# 55. Principe fondamental de migration

Pour chaque projet logique :

```text
Source
    +
Reference variants
    ↓
Target Framework union
    ↓
Dependency compatibility
    ↓
Package compatibility
    ↓
Code compatibility
    ↓
Destination
```

La règle centrale reste :

```text
TFM(C) =
    Unique(
        TFM(A)
        ∪
        TFM(all matching B projects)
    )
```

Le résultat final doit préserver le comportement legacy tout en incorporant les variantes modernes de B lorsque celles-ci sont compatibles.

---

# 56. Résultat attendu

Le produit final est un CLI capable de transformer automatiquement une solution legacy en solution multi-target tout en conservant une approche prudente et traçable.

Le système doit privilégier :

```text
Automatisation
       +
Déterminisme
       +
Traçabilité
       +
Validation
       +
Interactivité uniquement lorsque nécessaire
```

plutôt qu'une migration agressive.

Le CLI doit toujours permettre à l'utilisateur de comprendre :

1. ce qui a été détecté ;
2. pourquoi deux projets ont été associés ;
3. pourquoi un TFM a été ajouté ;
4. pourquoi une version de package a été conservée ;
5. pourquoi un changement de code est nécessaire ;
6. pourquoi une migration est bloquée ;
7. exactement quels fichiers vont être modifiés.