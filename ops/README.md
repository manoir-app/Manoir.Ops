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

Gaia also manages plugin source repositories under the plugins child folder of that same root:

- default repository list: https://github.com/manoir-app/Manoir.PluginCatalog
- environment override: MANOIR_PLUGINS_REPO as a comma-separated list of Git URLs
- local runner override: ./ops/run-local-gaia-agent.ps1 -PluginsRepo <url1>,<url2>
- managed clones are synchronized under plugins/_managed so manual local plugin folders can coexist
- on startup, Gaia synchronizes the configured repositories when the local plugin catalog is still empty
- the Ops Admin UI now exposes an API and buttons to inspect, edit, persist, and force-resync that repository list

## Admin UI Exposure Model

Plugin manifests can now declare how an Admin UI is meant to be exposed behind a shared reverse proxy.

The deployment section accepts an optional adminUi block:

```yaml
deployment:
	group: home-automation
	adminUi:
		pathPrefix: /home-automation
		composeService: admin-ui
		port: 8080
	artifacts:
		- kind: compose
			path: deploy/docker-compose.yml
		- kind: env-template
			path: deploy/.env.template
```

Current meaning:

- pathPrefix: public base path to expose behind the shared entrypoint, for example /platform or /home-automation;
- composeService: service name inside the plugin deployment that should receive Admin UI traffic;
- port: container port to target for that service.

Gaia now turns that contract into a local shared Traefik runtime:

- domain repositories still own their Admin UI pages and contributions;
- Gaia / PlatformOps owns the deployed base URL and reverse-proxy mapping;
- Gaia deploys a shared Traefik service on the manoir Docker network;
- plugin AdminUi routes are derived automatically from pathPrefix + composeService + port.

The plugin manifest still does not choose host ports, Traefik entrypoints, or other local exposure policy. Gaia keeps ownership of those runtime decisions.
