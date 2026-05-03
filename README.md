# MaNoir.PlatformOps

Ce repository porte la famille Platform Operations de MaNoir.

Il a vocation a heberger les composants de deploiement, de convergence runtime, de control plane, et plus generalement de pilotage technique de la plateforme. Il ne doit pas devenir un nouveau Core transverse ni un domaine metier masque.

## Role du repository

Ce repo est destine a contenir principalement :

- le noyau applicatif de PlatformOps ;
- les contrats publics de control plane quand ils doivent etre consommes depuis d'autres repos ;
- les providers techniques lies aux cibles d'execution ;
- une API de pilotage si une surface distante est necessaire ;
- une interface d'exploitation si un back-office ops apparait.

Exemples de projets cibles :

- packages/MaNoir.PlatformOps.Core
- packages/MaNoir.PlatformOps.Contracts
- packages/MaNoir.PlatformOps.Provider.Kubernetes
- packages/MaNoir.PlatformOps.Provider.Docker
- apps/MaNoir.PlatformOps.Api
- apps/MaNoir.PlatformOps.AdminUi
- ui/MaNoir.PlatformOps.AdminUi

## Ce que ce repository ne doit pas devenir

Ce repo ne doit pas absorber :

- la logique metier des grands domaines ;
- les primitives transverses generales du Core ;
- les dashboards et experiences front composees ;
- la correlation et le routage des signaux externes du Communication Hub ;
- des agents transverses qui n'ont pas pour role premier le pilotage de l'execution de la plateforme.

Si un besoin n'agit ni sur le deploiement, ni sur la convergence, ni sur le runtime, il doit probablement vivre ailleurs.

## Principes directeurs

Les regles structurantes de MaNoir V2 sont les suivantes :

1. PlatformOps pilote l'execution de la plateforme, pas sa verite metier.
2. Le Core reste proprietaire des primitives transverses generales.
3. Les domaines restent proprietaires de leur verite metier.
4. Les agents orchestrent, mais ne deviennent pas source de verite technique canonique.
5. Les interfaces d'exploitation pilotent via des surfaces publiques, sans acceder aux implementations internes des autres familles.
6. Les providers d'execution restent separables par cible technique.

## Packaging

La strategie cible est le multirepo avec packages NuGet prives.

Dans ce cadre :

- le projet Contracts est publiable quand des surfaces de control plane doivent etre partagees ;
- un projet Client n'est a introduire que s'il existe plusieurs consommateurs reels ;
- les providers et implementations runtime restent internes par defaut ;
- l'API et l'AdminUi restent internes au repo ;
- on evite de publier des packages d'implementation lourds par simple commodite.

## Repository Layout

La structure racine recommandee pour les repos MaNoir est la suivante :

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

Regles de base :

- les applications executables vivent sous `apps/` ;
- les librairies et packages vivent sous `packages/` ;
- les SPA et frontends web vivent sous `ui/` ;
- les tests vivent sous `tests/` ;
- les artefacts d'exploitation vivent sous `ops/` ;
- le back-office s'appelle toujours `AdminUi` ;
- on evite les dossiers racine concurrents comme `bo/`, `pages/`, `frontend/`, `backend/`, `api/`, `domain/`, ou `services/`.

Modele recommande pour l'admin web :

- `apps/MaNoir.PlatformOps.AdminUi/` est le host web .NET ;
- `ui/` contient un ou plusieurs modules frontend compiles separement ;
- on n'introduit l'AdminUi que lorsqu'un besoin d'exploitation humain apparait reellement.

## Documentation

Le document de reference pour la vision d'ensemble est [ARCHITECTURE.md](ARCHITECTURE.md).

Il decrit notamment :

- la big picture de MaNoir ;
- le decoupage des responsabilites ;
- le positionnement de Platform Operations ;
- la strategie multirepo ;
- les regles de dependance ;
- les conventions de publication des packages.

## Copilot Customization

Ce repo embarque un premier socle de customisation Copilot dans `.github/` :

- `copilot-instructions.md` pour les invariants globaux du repo ;
- des fichiers `.instructions.md` pour les regles ciblees ;
- des `SKILL.md` pour les workflows repetables comme le placement d'un composant ou le bootstrap d'un nouveau repo.

L'objectif est de rendre le bootstrap et les futurs decoupages de PlatformOps plus reproductibles.

## Intention

Le but de cette base n'est pas d'etre parfaite des le premier jour.
Le but est de rendre explicite la frontiere PlatformOps tres tot, afin d'eviter que les sujets de deployment et de runtime ne retombent soit dans le Core, soit dans un repo metier par commodite.

