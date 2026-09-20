# Releases

The `main` branch contains stable development. The `prerelease` branch is available for preview builds.

## Stable release

Create and push a numeric tag without a suffix, for example `v2.1.0`. GitHub Actions builds the self-contained application and creates a normal GitHub release.

## Pre-release

Create and push a tag with a suffix from the `prerelease` branch, for example `v2.1.0-beta.1`. GitHub Actions marks that release as a pre-release.

Application releases contain only the LogViewer executable.

## Event catalog releases

Add new or updated `.evdb` files below `EventCatalogs` and push the commit to `main`. A separate workflow creates an incremental `catalog-*` release containing only the files changed in that commit. Previous catalog releases stay online, so existing `.evdb` files do not need to be uploaded again. The LogViewer combines the assets from all releases and lets users download them by device family.
