# Operations Artifacts

This folder will host runtime and deployment artifacts owned by this repository.

The first concrete local artifacts are now:

- build-local-gaia-image.ps1 to build the local manoir-agents-gaia image from apps/MaNoir.PlatformOps.AdminUi;
- run-local-gaia-agent.ps1 to run Gaia in Docker with the required PlatformOps environment variables and Docker socket mount.

The local Gaia runner now maps a persistent home-automation root into the container:

- Linux host: /home-automation mounted to /home-automation;
- Windows host: %ProgramData%/MaNoir/home-automation mounted to /home-automation.

Shared services live under the shared-services child folder of that root.
