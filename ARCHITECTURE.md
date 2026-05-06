# MaNoir - Target Architecture

## Purpose of This Document

This document defines the overall vision for MaNoir V2.

The goal is not to describe an ideal or exhaustive architecture, but to establish simple rules that help avoid rebuilding a functional monolith where business logic, frontend, APIs, orchestration, and integrations are all mixed together.

The guiding principle is the following:

- keep a strong but bounded transverse foundation;
- separate the main business domains;
- isolate orchestration agents;
- isolate composed user experiences;
- make dependencies visible through versioned private packages.

## Problem to Solve

The main difficulty observed in previous iterations was not only technical.
It was mostly organizational: too many responsibilities ended up living in a single project.

Concretely, the same codebase contained:

- business logic;
- frontend code;
- APIs;
- external interactions;
- communication between agents;
- orchestration;
- dashboards and other composed experiences.

The goal of V2 is to push that drift as far away as possible by making the following explicit:

- functional owners;
- public surfaces;
- allowed dependencies;
- experimental zones.

## Overview

MaNoir is organized around six groups of responsibilities.

### 1. Platform Core

The Platform Core contains the building blocks without which the rest cannot exist.

It notably includes:

- identity, users, households, privacy, permissions, preferences;
- transverse places and topology;
- transverse coordination objects, such as action items;
- notifications;
- configuration and administration;
- inter-process communication.

The Platform Core is not meant to carry the business logic of the main domains.

### 2. Communication Hub

The Communication Hub centralizes external interactions.

It notably handles:

- multi-channel ingestion;
- normalization;
- correlation;
- deduplication;
- traceability and provenance.

It can reconcile multiple external signals that probably refer to the same thing, but it is not the owner of the final business meaning.

### 3. Business Domains

The main business domains own the objects, rules, and functional usages specific to each subsystem.

Examples of expected domains:

- Home;
- Stock;
- Possessions;
- Family.

Each domain owns its business truth.

### 4. Platform Operations

The Platform Operations family groups the operating and control-plane components of the platform.

It notably covers:

- platform deployment;
- convergence toward an execution target;
- Kubernetes or Docker operation depending on the chosen mode;
- operations configuration;
- technical runtime state.

These components belong neither to business logic nor to the transverse Core. They operate the platform, but must not become the owners of business objects.

### 5. Operational Agents

Agents orchestrate, monitor, plan, and chain actions.

They may consume multiple domains and the transverse foundation, but they must not become the functional source of truth of the system.

### 6. Composed Experiences

Composed experiences group transverse, usage-oriented interfaces:

- dashboards;
- tablets;
- frontend experiences;
- usage-oriented adapters or facades.

They compose existing capabilities, but do not redefine business models.

## Structural Rules

The following rules serve as the minimal constitution of the system.

1. The Core provides transverse primitives, not hidden business logic.
2. A domain owns its business logic.
3. The Communication Hub correlates and routes, but does not decide final business meaning on its own.
4. Platform Operations components drive platform execution, not business truth.
5. An agent orchestrates, but does not become a source of truth.
6. A UI composes and drives through public surfaces, but does not own canonical business logic.
7. Across repositories, depend on versioned public contracts, not on internal implementations.

## Repository Split Strategy

The chosen strategy is a multi-repository setup with private NuGet packages.

This choice allows:

- working on different domains separately;
- versioning public surfaces;
- making coupling visible;
- iterating quickly without rebuilding a single giant solution.

The target structure is the following.

### Platform Repository

The platform repository hosts the transverse foundation and the external interaction hub.

Examples of projects:

- `MaNoir.Core`
- `MaNoir.Core.Contracts`
- `MaNoir.Core.Client` if needed
- `MaNoir.Core.Api`
- `MaNoir.Core.AdminUi`
- `MaNoir.Core.AdminUi.Hosting` if a transverse NuGet foundation is needed to standardize administration hosts
- `MaNoir.CommunicationHub`
- `MaNoir.CommunicationHub.Contracts`
- `MaNoir.CommunicationHub.Client` if needed
- `MaNoir.CommunicationHub.Api` if needed

### Domain Repositories

Each major domain has its own repository.

Examples of structure:

- `MaNoir.Stock.Domain`
- `MaNoir.Stock.Contracts`
- `MaNoir.Stock.Client` if needed
- `MaNoir.Stock.Api`
- `MaNoir.Stock.AdminUi`
- `MaNoir.Stock.AgentLocal` if needed

The same principle applies to `MaNoir.Home`, `MaNoir.Possessions`, `MaNoir.Family`, and so on.

### Platform Operations Repository

Platform operating components live in a dedicated family, separate from the Core and from business agents.

A typical example is a component such as Gaia, responsible for converging the platform configuration toward a Kubernetes or Docker target.

Examples of structure:

- `MaNoir.PlatformOps.Core`
- `MaNoir.PlatformOps.Contracts`
- `MaNoir.PlatformOps.Api` if an operating surface is needed
- `MaNoir.PlatformOps.Provider.Kubernetes`
- `MaNoir.PlatformOps.Provider.Docker`
- `MaNoir.PlatformOps.AdminUi` if an operations interface appears

This family owns deployment, convergence, and control-plane concerns.
It must not absorb business logic from the domains or primitives from the transverse Core.

### Transverse Agent Repositories

Agents are not isolated by default into one repository per agent.

The rule is the following:

- a single-domain agent lives in the domain repository;
- a transverse agent lives in a transverse agents repository;
- a new agents repository is created only when it matches a real stable boundary.

Examples:

- `MaNoir.Agents.Local`
- `MaNoir.Agents.Remote` if a real need appears later

Each agents repository may host several related agents, along with a shared runtime if that makes sense.

### Experiences Repository

Usage-oriented frontend interfaces live in a repository dedicated to composed experiences.

Examples:

- `MaNoir.Experience.Shell`
- `MaNoir.Experience.Tablet`
- `MaNoir.Experience.Dashboard`

### Lab Repository

A quarantine repository will probably end up existing.

Rather than denying it, it is better to acknowledge it in a controlled form, for example:

- `MaNoir.Lab`
- `MaNoir.Experimental`

This repository does not have the same status as the others.

Associated rules:

- no other repository must depend on it for a central need;
- it must not publish a canonical package;
- everything that enters it must have an owner and an exit criterion.

## Package Publishing Strategy

Not every project is meant to be published.

### Publishable Projects

Always publishable:

- `MaNoir.X.Contracts`

Publishable only when several real consumers exist:

- `MaNoir.X.Client`

Publishable by explicit exception when they carry stable transverse infrastructure:

- `MaNoir.Core.AdminUi.Hosting` for the .NET foundations used to build `MaNoir.X.AdminUi`

### Projects Not Publishable by Default

These projects stay internal to the repository:

- `MaNoir.X.Domain`
- `MaNoir.X.Api`
- `MaNoir.X.AdminUi`
- `MaNoir.X.AgentLocal`

The goal is to avoid turning each repository into a mini-framework.

### Special Case: Transverse AdminUi Foundation

A package such as `MaNoir.Core.AdminUi.Hosting` is legitimate if, and only if, it carries a transverse technical foundation that several layers genuinely need in order to build their `AdminUi` host.

Its role remains general: provide the base components needed to create the .NET web host for AdminUi applications, without absorbing screens or business logic.

### Special Case: Transverse Frontend Foundation

The model also allows for a shared frontend package, this time as an npm package.

The natural target name is `ui/MaNoir.Core.AdminUi.Shared/` when it serves as a common foundation for MaNoir back offices.

Its role remains general: contain reusable React/UI components, controls, and utilities without absorbing screens or domain business logic.

The separation rule remains the following:

- the NuGet `MaNoir.Core.AdminUi.Hosting` carries the .NET host foundations;
- the npm package `MaNoir.Core.AdminUi.Shared` carries the React/UI foundations;
- functional modules remain under `ui/MaNoir.X.AdminUi.<Feature>/`.

## Folder Structure Convention

All MaNoir repositories should follow the same root vocabulary to avoid random variations such as `ui`, `bo`, `pages`, `api`, `domain`, or `services` being introduced at repository level.

Recommended root structure:

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

Not all of these folders are required in every repository. However, when a need appears, it should use this vocabulary rather than a new local convention.

Associated rules:

1. `apps/` contains executable, hostable, or runnable components, for example APIs, workers, hosts, and .NET applications.
2. `packages/` contains reusable, shareable, or publishable components, for example `Domain`, `Contracts`, `Client`, and other libraries.
3. `ui/` contains web frontends, SPA modules, React or Vue back offices, design systems, and equivalent frontend packages.
4. `tests/` contains all test projects, regardless of technology.
5. `ops/` contains manifests, deployment scripts, docker compose files, helm charts, and operations artifacts owned by the repository.
6. `docs/` contains repository-specific documentation when it goes beyond the README and the root architecture document.
7. `eng/` contains build tooling, automation scripts, and engineering helpers.
8. Do not introduce competing root folders such as `bo/`, `pages/`, `backend/`, `frontend/`, `services/`, or `src/` as a single generic block.

Naming and placement convention:

1. Every project is placed in a folder with the exact same name as the project.
2. Executable .NET components typically live in `apps/<ExactProjectName>/`.
3. .NET libraries and packages typically live in `packages/<ExactProjectName>/`.
4. SPAs, frontend modules, and JavaScript or TypeScript packages typically live in `ui/<ExactProjectName>/`.
5. Test projects follow the same logic under `tests/`, for example `tests/MaNoir.X.Domain.Tests/` or `tests/MaNoir.X.AdminUi.Tests/`.
6. The back office is always named `AdminUi` as a project name, never `Bo`, `BackOffice`, `Ui`, or `Pages`.
7. Composed frontend experiences are named `Experience.*`.
8. Runtime operating components are named `PlatformOps.*`.

Recommended model for web administration:

1. `apps/MaNoir.X.AdminUi/` designates the .NET web host of the back office.
2. This host serves the built assets of one or more frontend modules located in `ui/`.
3. Administration frontend modules use an explicit functional suffix, for example `ui/MaNoir.X.AdminUi.Users/`, `ui/MaNoir.X.AdminUi.Settings/`, or `ui/MaNoir.X.AdminUi.Inventory/`.
4. A shared frontend package may exist under `ui/MaNoir.X.AdminUi.Shared/` for the design system, HTTP client, or common components of a domain.
5. A transverse platform npm package may also exist under `ui/MaNoir.Core.AdminUi.Shared/` for React/UI components, controls, and utilities shared across several AdminUi applications.
6. Avoid turning `MaNoir.X.AdminUi` into a single monolithic SPA if several functional modules can be built and served separately.

Practical build rule:

1. `dotnet` builds must be able to target `apps/`, `packages/`, and `tests/` without having to guess where the .NET projects are.
2. `npm`, `pnpm`, or `yarn` builds must be able to target `ui/` to find all SPAs and frontend packages.
3. The `MaNoir.X.AdminUi` host must be able to serve the build output of several `ui/` modules without forcing a single large SPA.

Example for a domain repository with a .NET API, a .NET admin host, and React modules:

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

If a back office is fully server-side, `apps/MaNoir.X.AdminUi/` may be enough. If the back office embeds React or Vue frontends, `apps/MaNoir.X.AdminUi/` remains the host and frontend modules live in `ui/`.

## Dependency Rules

Dependencies must remain readable and intentional.

### General Rules

1. `MaNoir.Core` does not depend on any business domain.
2. `MaNoir.CommunicationHub` may depend on the Core, but not on a domain in order to interpret final business meaning.
3. A domain may depend on the Core.
4. A domain may consume Communication Hub contracts.
5. Platform Operations may depend on the Core and the public contracts needed to operate the platform.
6. Platform Operations does not depend on internal business-domain code.
7. A domain does not depend directly on the internal code of another domain.
8. An agent may depend on the public contracts and clients of the blocks it orchestrates.
9. An agent never depends on a UI.
10. An administration UI depends on an API or a public client, never on the internal Domain.
11. An administration UI may also depend on a transverse technical host foundation, for example `MaNoir.Core.AdminUi.Hosting`, as long as that foundation remains purely technical.
12. A composed frontend experience depends on public surfaces, never on an internal Domain.

### Shape of Inter-Repository Exchanges

Inter-repository exchanges should preferably use:

- `Contracts` for DTOs, events, commands, and public identifiers;
- `Client` for HTTP or messaging consumption facades.

Avoid direct references to internal implementations.

## Versioning Convention

Public packages follow a simple SemVer logic.

1. contract break: major version;
2. compatible addition: minor version;
3. non-breaking fix: patch version.

Complementary rules:

- no silent breaking change on `Contracts`;
- `Client` packages should ideally follow the same major version as their associated `Contracts`;
- prereleases are allowed to iterate quickly.

## Quick Placement Guide

When a new component appears, the following questions help decide where it should live.

1. Is it a primitive without which the rest cannot exist?
Then it probably belongs to the transverse Core.

2. Is it a component that ingests, normalizes, correlates, or deduplicates external signals?
Then it probably belongs to the Communication Hub.

3. Is it a component that deploys, operates, or converges the platform toward a technical target?
Then it probably belongs to Platform Operations.

4. Is it a component that owns a specific business truth?
Then it probably belongs to a business domain.

5. Is it a component that orchestrates several blocks without owning their business truth?
Then it probably belongs to operational agents.

6. Is it a component that assembles several blocks to expose a user experience?
Then it probably belongs to composed experiences.

7. If no location is obvious, should it be placed temporarily in a quarantine zone?
Yes, but only in a `MaNoir.Lab`-like area, with an explicit owner and an exit criterion.

## What This Repository Should Carry

The current repository is meant to host the platform part of MaNoir.

It is therefore intended to host, first and foremost:

- the transverse Core;
- the Core public contracts;
- the Core API;
- the Core administration UI;
- the Communication Hub and its contracts.

It must not become the default repository for everything that has not found its place yet.
It must not absorb Platform Operations components either.

## What to Avoid

The following signs would indicate a drift that should be corrected quickly:

- the Core starts accumulating domain-specific fields or services;
- the Communication Hub starts creating business meaning instead of correlating;
- a deployment or control-plane component is placed in the Core;
- an agent becomes the owner of canonical state;
- a UI embeds deep business rules;
- a `Common`, `Shared`, or `Utils` project starts becoming indispensable to everyone;
- `MaNoir.Lab` becomes a central dependency.

## Recommended Start

It is not necessary to create all repositories and all projects on day one.

The most reasonable starting point is:

1. the platform repository;
2. a first real domain;
3. a Platform Operations repository if the platform includes a real control plane;
4. a transverse agents repository if a real need appears;
5. an experiences repository only when a real frontend UI exists.

The convention must be established immediately.
The full instantiation should remain progressive.

## Summary

The target is not an artificial multiplication of projects.
The target is an architecture that makes the following explicit:

- the transverse foundation;
- business domains;
- the interaction hub;
- platform operations;
- agent orchestration;
- composed experiences;
- versioned public surfaces.

The system will remain imperfect, and a quarantine space will probably eventually exist.
The goal is not to remove all ambiguity forever, but to make sure it arrives as late as possible and remains visible.
