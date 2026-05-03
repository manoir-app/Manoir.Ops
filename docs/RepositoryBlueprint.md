# Repository Blueprint

## Intent

This repository starts as the Platform Operations control-plane family for MaNoir.

The first slice is intentionally narrow:

- one core package for shared PlatformOps abstractions and orchestration;
- one Kubernetes provider package to prove provider-based separation;
- one Docker provider package for local or single-host runtime convergence;
- one unit test project for pure normalization and registry behavior;
- one functional test project for end-to-end package composition inside the repo.

## Initial Project Map

### Day-one projects

- `packages/MaNoir.PlatformOps.Core/`
- `packages/MaNoir.PlatformOps.Provider.Kubernetes/`
- `packages/MaNoir.PlatformOps.Provider.Docker/`
- `tests/MaNoir.PlatformOps.Core.UnitTests/`
- `tests/MaNoir.PlatformOps.Core.FunctionalTests/`

### Reserved next projects

- `packages/MaNoir.PlatformOps.Contracts/` when control-plane contracts must be consumed cross-repo;
- `packages/MaNoir.PlatformOps.Provider.Docker/` when Docker remains a real first-class runtime target;
- `apps/MaNoir.PlatformOps.Api/` when remote control-plane access is required;
- `apps/MaNoir.PlatformOps.AdminUi/` and `ui/MaNoir.PlatformOps.AdminUi/` when a human operations surface appears.

## First Slice Rules

- keep runtime-target abstractions in `MaNoir.PlatformOps.Core`;
- keep Kubernetes-specific normalization and conventions in the Kubernetes provider;
- keep tests split between pure unit coverage and cross-project functional coverage;
- keep `ops/` ready for deployment artifacts, but do not invent Helm or manifests before a real deployment target is fixed.

## Immediate Follow-up

1. Add `MaNoir.PlatformOps.Contracts` once another repo needs to consume deployment target contracts.
2. Introduce `MaNoir.PlatformOps.Api` only when remote orchestration flows are clear enough to stabilize an HTTP surface.
3. Clarify whether Docker support targets plain containers, Compose projects, or another hosted runtime shape before adding deployment commands.

