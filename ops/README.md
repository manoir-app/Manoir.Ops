# Operations Artifacts

This folder will host runtime and deployment artifacts owned by this repository.

The first concrete local artifacts are now:

- build-local-gaia-image.ps1 to build the local manoir-agents-gaia image from apps/MaNoir.PlatformOps.AdminUi;
- run-local-gaia-agent.ps1 to run Gaia in Docker with the required PlatformOps environment variables and Docker socket mount.

The CI workflow now builds and publishes Gaia for both:

- linux/amd64
- linux/arm64

The local build script also accepts an explicit Docker platform when needed, for example:

- ./ops/build-local-gaia-image.ps1 -Platform linux/arm64
- ./ops/build-local-gaia-image.ps1 -Platform linux/amd64

MongoDB image selection is configurable for shared services:

- default image: mongo:8
- explicit override: pass MANOIR_MONGO_IMAGE to Gaia, or use ./ops/run-local-gaia-agent.ps1 -MongoImage <image>
- for older Raspberry Pi generations, prefer documenting the required override in deployment configuration rather than changing the runtime default

The local Gaia runner now maps a persistent home-automation root into the container:

- Linux host: /srv/manoir/home-automation mounted to /home-automation;
- Windows host: %ProgramData%/MaNoir/home-automation mounted to /home-automation.

Shared services live under the shared-services child folder of that root.

## Admin UI Exposure Model

Plugin manifests can now declare how an Admin UI is meant to be exposed behind a shared reverse proxy.

The deployment section accepts an optional adminUi block:

```yaml
deployment:
	group: home-automation
	adminUi:
		pathPrefix: /home-automation
		service: admin-ui
		port: 8080
	artifacts:
		- kind: compose
			path: deploy/docker-compose.yml
		- kind: env-template
			path: deploy/.env.template
```

Current meaning:

- pathPrefix: public base path to expose behind the shared entrypoint, for example /platform or /home-automation;
- service: service name inside the plugin deployment that should receive Admin UI traffic;
- port: container port to target for that service.

This is the first concrete step toward Gaia-managed Traefik exposure:

- domain repositories still own their Admin UI pages and contributions;
- Gaia / PlatformOps owns the deployed base URL and reverse-proxy mapping;
- the manifest now carries enough information to generate HTTP routes later.

This model does not yet generate Traefik configuration on its own. It defines the deployment contract that future Gaia routing work can consume.
