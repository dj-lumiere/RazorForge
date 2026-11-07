#!/usr/bin/env bash
# Resolve the packaging version and GitHub-release branding from the pushed tag,
# writing PKG_VERSION / RELEASE_NAME / RELEASE_NOTES to $GITHUB_ENV for later steps.
#
# Two tag families feed the same release workflow (Revare's .github/workflows/release.yaml, which the
# Mielia repository calls for its tags). Runs from the workspace root that holds the projects side by side.
#   v*      -> Revare release. Artifacts named for the tag version.
#   me-v*   -> Mielia release. The SAME builder artifacts (one binary runs both
#              languages), named for the csproj builder version — NOT the mi tag,
#              whose "0.1.0" would collide with the existing v0.1.0 release — and
#              published with the Mielia notes and title.
set -euo pipefail

csproj_version() {
  sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' Revare/Revare.csproj | head -1
}

tag="${GITHUB_REF_NAME:?GITHUB_REF_NAME is not set}"

if [[ "$tag" == me-v* ]]; then
  pkg_version="$(csproj_version)"                 # e.g. 0.4.0 — the builder that runs .mie
  release_name="Mielia v${tag#me-v}"              # e.g. Mielia v0.5.0
  release_notes="Revare/scripts/package-assets/RELEASE_NOTES_MI.md"
else
  pkg_version="${tag#v}"                          # e.g. 0.4.0
  release_name="Revare ${tag}"
  release_notes="Revare/scripts/package-assets/RELEASE_NOTES.md"
fi

{
  echo "PKG_VERSION=${pkg_version}"
  echo "RELEASE_NAME=${release_name}"
  echo "RELEASE_NOTES=${release_notes}"
} >> "${GITHUB_ENV:?GITHUB_ENV is not set}"

echo "Resolved: tag=${tag} PKG_VERSION=${pkg_version} RELEASE_NAME='${release_name}' RELEASE_NOTES=${release_notes}"
