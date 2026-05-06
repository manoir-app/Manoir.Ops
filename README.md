# MaNoir.PlatformOps

This repository hosts the MaNoir Platform Operations family.

Its purpose is to contain deployment, runtime convergence, control plane, and more generally the technical operating components of the platform. It must not become a second transverse Core or a hidden business domain.

## Repository Role

This repository is primarily intended to contain:

- the PlatformOps application core;
- public control-plane contracts when they need to be consumed from other repositories;
- technical providers tied to execution targets;
- an operations API when a remote surface is required;
- an operations UI if a human-facing back office becomes necessary.

Examples of target projects:

- packages/MaNoir.PlatformOps.Core
- packages/MaNoir.PlatformOps.Contracts
- packages/MaNoir.PlatformOps.Provider.Kubernetes
- packages/MaNoir.PlatformOps.Provider.Docker
- apps/MaNoir.PlatformOps.Api
- apps/MaNoir.PlatformOps.AdminUi
- ui/MaNoir.PlatformOps.AdminUi

## What This Repository Must Not Become

This repository must not absorb:

- business logic from the main domains;
- general transverse Core primitives;
- composed frontend dashboards and experiences;
- correlation and routing of external signals from the Communication Hub;
- transverse agents whose primary role is not to operate platform execution.

If a need does not act on deployment, convergence, or runtime, it probably belongs elsewhere.

## Guiding Principles

The structuring rules for MaNoir V2 are the following:

1. PlatformOps operates platform execution, not business truth.
2. The Core remains the owner of general transverse primitives.
3. Domains remain the owners of their business truth.
4. Agents orchestrate, but must not become the canonical source of technical truth.
5. Operations interfaces drive the platform through public surfaces without reaching into internal implementations from other families.
6. Execution providers must remain separable by technical target.

## Packaging

The target strategy is a multi-repository setup with private NuGet packages.

Within that strategy:

- the Contracts project is publishable when control-plane surfaces must be shared;
- a Client project should only be introduced when multiple real consumers exist;
- providers and runtime implementations stay internal by default;
- the API and AdminUi stay internal to the repository;
- heavy implementation packages should not be published just for convenience.

## Repository Layout

The recommended root structure for MaNoir repositories is the following:

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

Basic rules:

- executable applications live under `apps/`;
- libraries and packages live under `packages/`;
- SPAs and web frontends live under `ui/`;
- tests live under `tests/`;
- operations artifacts live under `ops/`;
- the back office is always named `AdminUi`;
- avoid competing root folders such as `bo/`, `pages/`, `frontend/`, `backend/`, `api/`, `domain/`, or `services/`.

Recommended model for the web admin:

- `apps/MaNoir.PlatformOps.AdminUi/` is the .NET web host;
- `ui/` contains one or more separately built frontend modules;
- `AdminUi` should only be introduced once there is a real human operations need.

## Documentation

The reference document for the overall vision is [ARCHITECTURE.md](ARCHITECTURE.md).

It describes in particular:

- the MaNoir big picture;
- the responsibility split;
- the Platform Operations positioning;
- the multi-repository strategy;
- the dependency rules;
- the package publishing conventions.

## Copilot Customization

This repository also includes an initial Copilot customization baseline in `.github/`:

- `copilot-instructions.md` for repository-wide invariants;
- `.instructions.md` files for targeted rules;
- `SKILL.md` files for repeatable workflows such as placing a component or bootstrapping a new repository.

The goal is to make PlatformOps bootstrapping and future splits more reproducible.

## Intention

The goal of this baseline is not to be perfect on day one.
The goal is to make the PlatformOps boundary explicit early, so deployment and runtime concerns do not slide back into either the Core or a business repository out of convenience.

