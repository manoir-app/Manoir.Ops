# MaNoir Repository Templates

This folder contains minimal starters for opening new MaNoir repositories without starting from scratch every time.

The goal is not to provide a full code skeleton, but rather a clean starting point for:

- repository framing;
- architecture boundaries;
- Copilot customization;
- baseline conventions.

## Principle

Each template must be treated as an intentional starting point, not as a blind copy-paste.

Before using one:

1. choose the repository family;
2. replace the placeholders;
3. remove what is not needed on day one;
4. keep only the projects and instructions justified by the actual need.

## Available Templates

- `platform-repo/`: for the transverse platform foundation, the Core, and the CommunicationHub;
- `domain-repo/`: for a major business domain;
- `platformops-repo/`: for control-plane and operations components;
- `agents-repo/`: for a transverse agents repository.
- `experiences-repo/`: for shells, dashboards, tablets, and other composed experiences.

## Recommended Usage

1. Start from the closest template.
2. Copy its content into the new repository.
3. Replace placeholders such as `MaNoir.Platform`, `{{DOMAIN_NAME}}`, `{{PACKAGE_PREFIX}}`.
4. Re-read the repository boundaries before creating projects.
5. Adjust or remove instructions that are too broad for the created repository.

## Recommended Root Structure

MaNoir templates start from the same root structure:

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

Naming rules:

- executable applications live under `apps/<ExactProjectName>/`;
- libraries and packages live under `packages/<ExactProjectName>/`;
- SPAs and web frontends live under `ui/<ExactProjectName>/`;
- tests live under `tests/<ExactProjectName>.Tests/`;
- the back office is always named `AdminUi`;
- do not introduce competing root folders such as `bo/`, `pages/`, `frontend/`, or `backend/`.

## Rules

- a template should help you start quickly, not lock in an unnecessary structure too early;
- a template does not replace [ARCHITECTURE.md](../../ARCHITECTURE.md);
- if a new repository does not fit any template, clarify its family first instead of forcing an unsuitable starter.

## Future Completion

If needed, this folder may later host:

- package publishing checklists;
- `.csproj` or solution skeletons if they become useful.
