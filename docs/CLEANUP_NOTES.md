# Cleanup Notes

## Safe To Delete

These paths are generated output/cache and should not be committed:

```text
bin/
obj/
build/
dist/
publish/
.codegraph/
TestResults/
*.binlog
*.log
*.msi
*.deb
*.zip
```

## Kept Intentionally

These paths look auxiliary, but are source, documentation, or local workflow configuration:

```text
.github/       repository prompts/agent docs
.specify/      project/spec workflow files
.vscode/       ignored local editor state; keep only if team wants shared settings
agent.md       project context/instructions
native/        optional native WASAPI helper
specs/         feature specs and plans
```

## Packaging Structure

```text
docs/                  human documentation
scripts/windows/       Windows build/package scripts
scripts/linux/         Linux package scripts
packaging/windows/     WiX MSI definitions
packaging/linux/       Debian package metadata
build/                 temporary staging output
dist/                  final release artifacts
```

## Current Caution Items

- `.specify/`, `.github/agents`, and `specs/` are not deleted because they appear to be planning/specification workflow files.
- `.vscode/` is ignored. If team settings are desired, explicitly unignore selected files later.
- `native/wasapi_autoeq/wasapi_autoeq.exe` is intentionally allowed by `.gitignore` because the project already had an exception for it.
