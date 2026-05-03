# MaNoir - Architecture cible

## Objet du document

Ce document fixe la vision d'ensemble de MaNoir pour la V2.

L'objectif n'est pas de decrire une architecture ideale ou exhaustive, mais de poser des regles simples pour eviter de reconstruire un monolithe fonctionnel dans lequel se melangent metier, front, API, orchestration et integrations.

Le principe directeur est le suivant :

- garder un socle transverse fort mais limite ;
- separer les grands domaines metier ;
- isoler les agents d'orchestration ;
- isoler les experiences utilisateur composees ;
- rendre visibles les dependances via des packages prives versionnes.

## Probleme a resoudre

La difficulte principale constatee sur les iterations precedentes n'etait pas seulement technique.
Le probleme etait surtout organisationnel : trop de responsabilites ont fini par vivre dans un seul projet.

Concretement, le meme ensemble contenait :

- du metier ;
- du front ;
- des API ;
- des interactions externes ;
- de la communication entre agents ;
- de l'orchestration ;
- des dashboards et autres experiences composees.

Le but de cette V2 est de repousser le plus loin possible cette derive en rendant explicites :

- les proprietaires fonctionnels ;
- les surfaces publiques ;
- les dependances autorisees ;
- les zones experimentales.

## Vue d'ensemble

MaNoir est organise autour de six ensembles de responsabilites.

### 1. Platform Core

Le Platform Core contient les briques sans lesquelles le reste ne peut pas exister.

Il regroupe notamment :

- identite, users, foyers, privacy, permissions, preferences ;
- lieux et topologie transverse ;
- objets transverses de coordination, comme les action items ;
- notifications ;
- configuration et administration ;
- communication inter-process.

Le Platform Core n'a pas vocation a porter la logique metier des grands domaines.

### 2. Communication Hub

Le Communication Hub centralise les interactions externes.

Il gere notamment :

- ingestion multi-canal ;
- normalisation ;
- correlation ;
- deduplication ;
- tracabilite et provenance.

Il sait rapprocher plusieurs signaux externes qui parlent probablement de la meme chose, mais il n'est pas proprietaire du sens metier final.

### 3. Domaines metier

Les grands domaines metier portent les objets, regles et usages fonctionnels propres a chaque sous-ensemble du systeme.

Exemples de domaines envisages :

- Home ;
- Stock ;
- Possessions ;
- Family.

Chaque domaine est proprietaire de sa verite metier.

### 4. Platform Operations

La famille Platform Operations regroupe les composants d'exploitation et de control plane de la plateforme.

Elle couvre notamment :

- le deploiement de la plateforme ;
- la convergence vers une cible d'execution ;
- le pilotage Kubernetes ou Docker selon le mode choisi ;
- la configuration d'exploitation ;
- l'etat technique du runtime.

Ces composants ne relevent ni du metier, ni du Core transverse. Ils pilotent la plateforme, mais ne doivent pas devenir proprietaires des objets metier.

### 5. Agents operationnels

Les agents orchestrent, surveillent, planifient et enchainent.

Ils peuvent consommer plusieurs domaines et le socle transverse, mais ils ne doivent pas devenir la source de verite fonctionnelle du systeme.

### 6. Experiences composees

Les experiences composees regroupent les interfaces transverses orientees usage :

- dashboards ;
- tablettes ;
- experiences front ;
- adaptateurs ou facades orientes usage.

Elles composent les capacites existantes, mais ne redefinissent pas les modeles metier.

## Regles de structure

Les regles suivantes servent de constitution minimale du systeme.

1. Le Core fournit des primitives transverses, pas du metier cache.
2. Un domaine est proprietaire de son metier.
3. Le Communication Hub correle et route, mais ne decide pas seul du sens metier final.
4. Les composants de Platform Operations pilotent l'execution de la plateforme, pas son metier.
5. Un agent orchestre, mais ne devient pas source de verite.
6. Une UI compose et pilote via les surfaces publiques, mais ne possede pas de logique metier canonique.
7. Entre repos, on depend de contrats publics versionnes, pas d'implementations internes.

## Strategie de decoupage des repos

La strategie retenue est le multirepo avec packages NuGet prives.

Ce choix permet :

- de travailler sur des domaines differents de facon separee ;
- de versionner les surfaces publiques ;
- de rendre le couplage visible ;
- d'iterer rapidement sans reconstruire une solution unique geante.

La structure cible est la suivante.

### Repo plateforme

Le repo plateforme heberge le socle transverse et le hub d'interactions externes.

Exemples de projets :

- `MaNoir.Core`
- `MaNoir.Core.Contracts`
- `MaNoir.Core.Client` si necessaire
- `MaNoir.Core.Api`
- `MaNoir.Core.AdminUi`
- `MaNoir.Core.AdminUi.Hosting` si un socle NuGet transverse est necessaire pour standardiser les hosts d'administration
- `MaNoir.CommunicationHub`
- `MaNoir.CommunicationHub.Contracts`
- `MaNoir.CommunicationHub.Client` si necessaire
- `MaNoir.CommunicationHub.Api` si necessaire

### Repos de domaines

Chaque grand domaine dispose de son propre repo.

Exemples de structure :

- `MaNoir.Stock.Domain`
- `MaNoir.Stock.Contracts`
- `MaNoir.Stock.Client` si necessaire
- `MaNoir.Stock.Api`
- `MaNoir.Stock.AdminUi`
- `MaNoir.Stock.AgentLocal` si besoin

Le meme principe s'applique a `MaNoir.Home`, `MaNoir.Possessions`, `MaNoir.Family`, etc.

### Repo Platform Operations

Les composants de pilotage de plateforme vivent dans une famille dediee, distincte du Core et des agents metier.

Exemple typique : un composant comme Gaia, charge de converger la configuration de la plateforme vers une cible Kubernetes ou Docker, releve de cette famille.

Exemples de structure :

- `MaNoir.PlatformOps.Core`
- `MaNoir.PlatformOps.Contracts`
- `MaNoir.PlatformOps.Api` si une surface de pilotage est necessaire
- `MaNoir.PlatformOps.Provider.Kubernetes`
- `MaNoir.PlatformOps.Provider.Docker`
- `MaNoir.PlatformOps.AdminUi` si une interface d'exploitation apparait

Cette famille porte les sujets de deploiement, de convergence et de control plane.
Elle ne doit pas absorber la logique metier des domaines ni les primitives du Core transverse.

### Repos d'agents transverses

Les agents ne sont pas isoles par defaut dans un repo par agent.

La regle retenue est la suivante :

- un agent mono-domaine vit dans le repo du domaine ;
- un agent transverse vit dans un repo d'agents transverses ;
- un nouveau repo d'agents n'est cree que s'il correspond a une vraie frontiere stable.

Exemples :

- `MaNoir.Agents.Local`
- `MaNoir.Agents.Remote` si un vrai besoin apparait plus tard

Chaque repo d'agents peut heberger plusieurs agents proches, ainsi qu'un runtime commun si cela a du sens.

### Repo Experiences

Les interfaces front orientees usage vivent dans un repo dedie aux experiences composees.

Exemples :

- `MaNoir.Experience.Shell`
- `MaNoir.Experience.Tablet`
- `MaNoir.Experience.Dashboard`

### Repo Lab

Il est probable qu'un depot de quarantaine finisse par exister.

Plutot que de le nier, il est preferable de l'assumer sous une forme controlee, par exemple :

- `MaNoir.Lab`
- `MaNoir.Experimental`

Ce depot n'a pas le meme statut que les autres.

Regles associees :

- aucun autre repo ne doit dependre de lui pour un besoin central ;
- il ne doit pas publier de package canonique ;
- tout ce qui y entre doit avoir un proprietaire et un critere de sortie.

## Strategie de publication des packages

Tous les projets n'ont pas vocation a etre publies.

### Projets publiables

Toujours publiables :

- `MaNoir.X.Contracts`

Publiables seulement s'il existe plusieurs consommateurs reels :

- `MaNoir.X.Client`

Publiables par exception explicite lorsqu'ils portent une infrastructure transverse stable :

- `MaNoir.Core.AdminUi.Hosting` pour les bases .NET de creation des `MaNoir.X.AdminUi`

### Projets non publiables par defaut

Ces projets restent internes au repo :

- `MaNoir.X.Domain`
- `MaNoir.X.Api`
- `MaNoir.X.AdminUi`
- `MaNoir.X.AgentLocal`

Le but est d'eviter de transformer chaque repo en mini-framework.

### Cas particulier du socle AdminUi transverse

Un package comme `MaNoir.Core.AdminUi.Hosting` est legitime si, et seulement si, il porte un socle technique transverse dont plusieurs couches ont reellement besoin pour construire leur host `AdminUi`.

Son role reste general : fournir les composants de base pour creer le web host .NET des AdminUi, sans absorber d'ecrans ou de logique metier.

### Cas particulier du socle frontend transverse

Le dispositif prevoit aussi un package frontend partage, cette fois sous forme de package npm.

Le nom cible naturel est `ui/MaNoir.Core.AdminUi.Shared/` lorsqu'il sert de socle commun aux back-offices MaNoir.

Son role reste general : contenir les composants, controles et utilitaires React/UI reutilisables, sans absorber d'ecrans ni de logique metier de domaine.

La regle de separation reste la suivante :

- le NuGet `MaNoir.Core.AdminUi.Hosting` porte les bases .NET du host ;
- le package npm `MaNoir.Core.AdminUi.Shared` porte les bases React/UI ;
- les modules fonctionnels restent dans `ui/MaNoir.X.AdminUi.<Feature>/`.

## Convention de structure de dossiers

Tous les repositories MaNoir devraient suivre le meme vocabulaire racine pour eviter les variations du type `ui`, `bo`, `pages`, `api`, `domain`, ou `services` poses au hasard au niveau du repo.

Structure racine recommandee :

```text
/
	.github/
	docs/
	eng/
	apps/
	packages/
	ui/
	tests/
	ops/
```

Tous ces dossiers ne sont pas obligatoires dans chaque repo. En revanche, si un besoin apparait, il doit utiliser ce vocabulaire plutot qu'une nouvelle convention locale.

Regles associees :

1. `apps/` contient les composants executables, hebergeables, ou lancables, par exemple les API, workers, hosts et applications .NET.
2. `packages/` contient les composants reutilisables, partageables ou publiables, par exemple `Domain`, `Contracts`, `Client` et autres librairies.
3. `ui/` contient les frontends web, modules SPA, back-offices React ou Vue, design systems, et packages frontend equivalents.
4. `tests/` contient tous les projets de test, quelle que soit leur techno.
5. `ops/` contient les manifests, scripts de deploiement, docker compose, helm charts, et artefacts d'exploitation portes par le repo.
6. `docs/` contient la documentation specifique au repo quand elle depasse le README et l'architecture racine.
7. `eng/` contient l'outillage de build, les scripts d'automatisation, et les helpers d'ingenierie.
8. On n'introduit pas de dossiers racine concurrents comme `bo/`, `pages/`, `backend/`, `frontend/`, `services/`, ou `src/` comme bloc generique unique.

Convention de nommage et de placement :

1. Chaque projet est place dans un dossier du meme nom exact que le projet.
2. Les composants .NET executables vont typiquement dans `apps/<ExactProjectName>/`.
3. Les librairies et packages .NET vont typiquement dans `packages/<ExactProjectName>/`.
4. Les SPA, modules front et packages JavaScript ou TypeScript vont typiquement dans `ui/<ExactProjectName>/`.
5. Les projets de test suivent la meme logique sous `tests/`, par exemple `tests/MaNoir.X.Domain.Tests/` ou `tests/MaNoir.X.AdminUi.Tests/`.
6. Le back-office s'appelle toujours `AdminUi` comme nom de projet, jamais `Bo`, `BackOffice`, `Ui`, ou `Pages`.
7. Les experiences front composees s'appellent `Experience.*`.
8. Les composants de pilotage runtime s'appellent `PlatformOps.*`.

Modele recommande pour l'administration web :

1. `apps/MaNoir.X.AdminUi/` designe le host web .NET du back-office.
2. Ce host sert les assets compiles de un ou plusieurs modules frontend situes dans `ui/`.
3. Les modules frontend d'administration suivent un suffixe fonctionnel explicite, par exemple `ui/MaNoir.X.AdminUi.Users/`, `ui/MaNoir.X.AdminUi.Settings/`, ou `ui/MaNoir.X.AdminUi.Inventory/`.
4. Un package partage frontend peut exister sous `ui/MaNoir.X.AdminUi.Shared/` pour le design system, le client HTTP, ou les composants communs d'un domaine.
5. Un package npm transverse de plateforme peut aussi exister sous `ui/MaNoir.Core.AdminUi.Shared/` pour les composants, controles et utilitaires React/UI communs a plusieurs AdminUi.
6. On evite de transformer `MaNoir.X.AdminUi` en une unique SPA monolithique si plusieurs modules fonctionnels peuvent etre compiles et servis separement.

Regle pratique de build :

1. Les builds `dotnet` doivent pouvoir cibler `apps/`, `packages/` et `tests/` sans devoir deviner ou sont les projets .NET.
2. Les builds `npm`, `pnpm` ou `yarn` doivent pouvoir cibler `ui/` pour retrouver toutes les SPA et packages frontend.
3. Le host `MaNoir.X.AdminUi` doit pouvoir servir le resultat de compilation de plusieurs modules `ui/` sans obliger a produire une seule grosse SPA.

Exemple pour un repo de domaine avec API .NET, host d'administration .NET et modules React :

```text
/
	.github/
	docs/
	eng/
	apps/
		MaNoir.Stock.Api/
		MaNoir.Stock.AdminUi/
	packages/
		MaNoir.Stock.Domain/
		MaNoir.Stock.Contracts/
	ui/
		MaNoir.Stock.AdminUi.Shared/
		MaNoir.Stock.AdminUi.Inventory/
		MaNoir.Stock.AdminUi.Shopping/
		MaNoir.Stock.AdminUi.Recipes/
	tests/
		MaNoir.Stock.Domain.Tests/
		MaNoir.Stock.Api.Tests/
		MaNoir.Stock.AdminUi.Tests/
```

Si un back-office est entierement server-side, `apps/MaNoir.X.AdminUi/` peut suffire. Si le back-office embarque des frontends React ou Vue, `apps/MaNoir.X.AdminUi/` reste le host et les modules frontend vivent dans `ui/`.

## Regles de dependance

Les dependances doivent rester lisibles et intentionnelles.

### Regles generales

1. `MaNoir.Core` ne depend d'aucun domaine metier.
2. `MaNoir.CommunicationHub` peut dependre du Core, mais pas d'un domaine pour interpreter le metier final.
3. Un domaine peut dependre du Core.
4. Un domaine peut consommer les contrats du Communication Hub.
5. Platform Operations peut dependre du Core et des contrats publics necessaires au pilotage de la plateforme.
6. Platform Operations ne depend pas du code interne des domaines metier.
7. Un domaine ne depend pas directement du code interne d'un autre domaine.
8. Un agent peut dependre des contrats et clients publics des blocs qu'il orchestre.
9. Un agent ne depend jamais d'une UI.
10. Une UI d'administration depend d'une API ou d'un client public, jamais du Domain interne.
11. Une UI d'administration peut aussi dependre d'un socle technique transverse de host, par exemple `MaNoir.Core.AdminUi.Hosting`, tant que ce socle reste purement technique.
12. Une experience front composee depend des surfaces publiques, jamais d'un Domain interne.

### Forme des echanges entre repos

Les echanges entre repos se font preferentiellement via :

- `Contracts` pour les DTO, evenements, commandes et identifiants publics ;
- `Client` pour les facades de consommation HTTP ou messaging.

On evite les references directes a des implementations internes.

## Convention de versioning

Les packages publics suivent une logique SemVer simple.

1. rupture de contrat : version majeure ;
2. ajout compatible : version mineure ;
3. correctif non cassant : version patch.

Regles complementaires :

- pas de breaking change silencieux sur les `Contracts` ;
- les `Client` suivent idealement la meme version majeure que leurs `Contracts` associes ;
- les prereleases sont autorisees pour iterer vite.

## Guide de placement rapide

Quand un nouveau composant apparait, les questions suivantes permettent de decider ou il doit vivre.

1. Est-ce une primitive sans laquelle le reste ne peut pas exister ?
Alors il releve probablement du Core transverse.

2. Est-ce un composant qui ingere, normalise, correle ou deduplique des signaux externes ?
Alors il releve probablement du Communication Hub.

3. Est-ce un composant qui deploie, pilote ou fait converger la plateforme vers une cible technique ?
Alors il releve probablement de Platform Operations.

4. Est-ce un composant proprietaire d'une verite metier specifique ?
Alors il releve probablement d'un domaine metier.

5. Est-ce un composant qui orchestre plusieurs blocs sans posseder leur verite metier ?
Alors il releve probablement des agents operationnels.

6. Est-ce un composant qui assemble plusieurs blocs pour exposer une experience utilisateur ?
Alors il releve probablement des experiences composees.

7. Si aucun emplacement n'est evident, faut-il le placer provisoirement dans une zone de quarantaine ?
Oui, mais uniquement dans une zone de type `MaNoir.Lab`, avec un proprietaire explicite et un critere de sortie.

## Ce que ce repo doit porter

Le present repo a vocation a porter la partie plateforme de MaNoir.

Il est donc destine a heberger prioritairement :

- le Core transverse ;
- les contrats publics du Core ;
- l'API du Core ;
- l'UI d'administration du Core ;
- le Communication Hub et ses contrats.

Il ne doit pas devenir le depot par defaut de tout ce qui n'a pas encore trouve sa place.
Il ne doit pas non plus absorber les composants de Platform Operations.

## Ce qu'il faut eviter

Les signes suivants indiqueraient une derive a corriger rapidement :

- le Core commence a accumuler des champs ou services specifiques a un domaine ;
- le Communication Hub commence a creer du metier au lieu de correler ;
- un composant de deploiement ou de control plane est range dans le Core ;
- un agent devient proprietaire d'un etat canonique ;
- une UI embarque des regles metier profondes ;
- un projet `Common`, `Shared` ou `Utils` commence a devenir indispensable a tout le monde ;
- `MaNoir.Lab` devient une dependance centrale.

## Demarrage recommande

Il n'est pas necessaire de creer tous les repos et tous les projets des le premier jour.

Le plus raisonnable est de demarrer avec :

1. le repo plateforme ;
2. un premier vrai domaine ;
3. un repo Platform Operations si la plateforme embarque un vrai control plane ;
4. un repo d'agents transverses si un besoin reel apparait ;
5. un repo experiences seulement quand une vraie UI front existe.

La convention doit etre posee tout de suite.
L'instanciation complete, elle, doit rester progressive.

## Resume

La cible n'est pas une multiplication artificielle des projets.
La cible est une architecture qui rend explicites :

- le socle transverse ;
- les domaines metier ;
- le hub d'interactions ;
- l'exploitation de la plateforme ;
- l'orchestration par agents ;
- les experiences composees ;
- les surfaces publiques versionnees.

Le systeme restera imparfait, et un espace de quarantaine finira probablement par exister.
Le but n'est pas de supprimer toute ambiguite pour toujours, mais de faire en sorte qu'elle arrive le plus tard possible et qu'elle reste visible.
