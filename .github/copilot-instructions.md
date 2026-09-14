# MaNoir.PlatformOps - Project Guidelines

## Scope

This repository hosts the Platform Operations family of MaNoir.

It is allowed to contain:

- deployment orchestration;
- runtime convergence;
- Kubernetes and Docker control components;
- control-plane APIs;
- technical platform state.

It must not become the default location for:

- business domain truth;
- platform-wide transverse primitives better owned by Core;
- composed front experiences;
- Communication Hub external signal correlation.

## Architecture

Use these rules:

- PlatformOps owns runtime control, not business state.
- A Gaia-like component belongs here by default.
- If a feature changes where or how the platform runs, it is probably PlatformOps.
- If a feature owns business truth, it belongs elsewhere.
- PlatformOps may depend on public contracts from other repos, but not on their internal implementation.
- Gaia is deployed before every other component (before Traefik, before Platform, before any plugin). It must never take a package dependency (NuGet or npm) published by Platform, HomeAutomation.Core, or any other repo it bootstraps. If Gaia needs a mechanism similar to one used elsewhere (for example Admin UI base-path hosting), it needs its own independent implementation in this repo, not a reference to a package from a bootstrapped repo.

## Plugin catalog manifest (manoir.plugin.yaml) — this repo does not own one

- This repo has NO `manoir.plugin.yaml` and must never get one. Manoir.Ops (Gaia) is a technical foundation / control-plane, not a deployable plugin — it does not go through the plugin catalog itself, it is the thing that reads the catalog and deploys everything else.
- This section exists here only so Gaia's behavior around plugin manifests is understood correctly, since Gaia code (`PlatformCoreCatalogPluginLoader`, `DockerDeploymentPlanFactory`, etc.) consumes catalog entries produced by other repos.
- Transformation flow happening in each *plugin* repo (Platform, HomeAutomation.Core, future plugin repos), not in this one:

```
<plugin repo>/manoir.plugin.yaml (PluginManifest, authored by hand)
        │  CI job "publish-plugin-catalog"
        │  invokes manoir-app/manoir-plugin-action
        ▼
Manoir.PluginCatalog/plugins/<Root>/<Category>/<PluginId>/plugin.yaml (AvailablePlugin, generated)
   + deploy/docker-compose.yml (copied from deployment.artifacts[].path)
   + readme.md (copied from README.md)
```

- Gaia (`PlatformCoreCatalogPluginLoader`) reads the generated `plugin.yaml` from `Manoir.PluginCatalog`, not the source `manoir.plugin.yaml` directly.
- If you are not sure whether a given manifest field is still used, whether the publish pipeline behaves as described above, or whether a change is safe, STOP and ask the user to confirm instead of assuming — the transformation logic lives in the external `manoir-app/manoir-plugin-action` repo (cloned locally for reference under `manoir-plugin-action/` at the workspace root) and must be checked there, not guessed.

## Packaging

Keep these rules:

- publish `MaNoir.PlatformOps.Contracts` only when control-plane contracts must be consumed from other repos;
- introduce `MaNoir.PlatformOps.Client` only if a real shared consumption need appears;
- keep providers and runtime implementation internal by default;
- avoid publishing implementation-heavy packages for convenience.

## Repository Layout

Prefer this root layout vocabulary:

- `apps/` for executable control-plane surfaces;
- `packages/` for reusable providers, contracts, and libraries;
- `ui/` for SPA-based operations frontends when they exist;
- `tests/` for test projects;
- `ops/` for deployment artifacts owned by the repo.