# Releases

The `main` branch contains stable development. The `prerelease` branch is available for preview builds.

## Stable release

Create and push a numeric tag without a suffix, for example `v2.1.0`. GitHub Actions builds the self-contained application and creates a normal GitHub release.

## Pre-release

Create and push a tag with a suffix from the `prerelease` branch, for example `v2.1.0-beta.1`. GitHub Actions marks that release as a pre-release.

Files in `EventCatalogs` ending in `.evdb` are attached to either kind of release. The LogViewer can download these catalogs independently of application updates.

