#!/bin/bash

# Script to create a new release with incremented version
# Usage: ./create-release.sh [--patch|--minor|--major]
# Default: --patch

set -e

# Color output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Check if we're in a git repository first
if ! git rev-parse --git-dir > /dev/null 2>&1; then
    echo -e "${RED}Error: Not in a git repository${NC}"
    exit 1
fi

# Get the git root directory and navigate there
GIT_ROOT=$(git rev-parse --show-toplevel)
cd "$GIT_ROOT"

# Project file path (relative to git root)
PROJECT_FILE="proxy/tvnoms-proxy.csproj"

# Parse arguments
INCREMENT_TYPE="patch"
if [ $# -gt 0 ]; then
    case "$1" in
        --patch)
            INCREMENT_TYPE="patch"
            ;;
        --minor)
            INCREMENT_TYPE="minor"
            ;;
        --major)
            INCREMENT_TYPE="major"
            ;;
        *)
            echo -e "${RED}Invalid argument: $1${NC}"
            echo "Usage: $0 [--patch|--minor|--major]"
            exit 1
            ;;
    esac
fi

echo -e "${GREEN}Creating new release (incrementing $INCREMENT_TYPE version)...${NC}"

# Check for uncommitted changes
if [[ -n $(git status -s) ]]; then
    echo -e "${YELLOW}Warning: You have uncommitted changes${NC}"
    read -p "Do you want to commit them before creating the release? (y/n) " -n 1 -r
    echo
    if [[ $REPLY =~ ^[Yy]$ ]]; then
        git add -A
        read -p "Enter commit message: " commit_msg
        git commit -m "$commit_msg"
    else
        echo -e "${RED}Please commit or stash your changes before creating a release${NC}"
        exit 1
    fi
fi

# Check if GitHub CLI is installed
if ! command -v gh &> /dev/null; then
    echo -e "${RED}Error: GitHub CLI (gh) is not installed${NC}"
    echo "Install it from: https://cli.github.com/"
    exit 1
fi

# Check if authenticated with GitHub
if ! gh auth status &> /dev/null; then
    echo -e "${RED}Error: Not authenticated with GitHub${NC}"
    echo "Run: gh auth login"
    exit 1
fi

# Extract current version from csproj file
CURRENT_VERSION=$(grep -oP '<Version>\K[^<]+' "$PROJECT_FILE")

if [ -z "$CURRENT_VERSION" ]; then
    echo -e "${RED}Error: Could not find version in $PROJECT_FILE${NC}"
    exit 1
fi

echo -e "Current version: ${YELLOW}$CURRENT_VERSION${NC}"

# Split version into components
IFS='.' read -r -a VERSION_PARTS <<< "$CURRENT_VERSION"
MAJOR="${VERSION_PARTS[0]}"
MINOR="${VERSION_PARTS[1]}"
PATCH="${VERSION_PARTS[2]}"

# Increment based on type
case "$INCREMENT_TYPE" in
    major)
        MAJOR=$((MAJOR + 1))
        MINOR=0
        PATCH=0
        ;;
    minor)
        MINOR=$((MINOR + 1))
        PATCH=0
        ;;
    patch)
        PATCH=$((PATCH + 1))
        ;;
esac

NEW_VERSION="${MAJOR}.${MINOR}.${PATCH}"
echo -e "New version: ${GREEN}$NEW_VERSION${NC}"

# Update version in csproj file
if [[ "$OSTYPE" == "darwin"* ]]; then
    # macOS
    sed -i '' "s|<Version>$CURRENT_VERSION</Version>|<Version>$NEW_VERSION</Version>|g" "$PROJECT_FILE"
else
    # Linux
    sed -i "s|<Version>$CURRENT_VERSION</Version>|<Version>$NEW_VERSION</Version>|g" "$PROJECT_FILE"
fi

echo -e "${GREEN}Updated version in $PROJECT_FILE${NC}"

# Commit the version change
git add "$PROJECT_FILE"
git commit -m "Bump version to $NEW_VERSION"

# Push to remote
echo -e "${GREEN}Pushing changes to remote...${NC}"
CURRENT_BRANCH=$(git rev-parse --abbrev-ref HEAD)
git push origin "$CURRENT_BRANCH"

# Create GitHub release
echo -e "${GREEN}Creating GitHub release v$NEW_VERSION...${NC}"

# Get the previous release tag
PREVIOUS_TAG=$(git describe --tags --abbrev=0 2>/dev/null || echo "")

# Generate changelog from git commits
if [ -z "$PREVIOUS_TAG" ]; then
    # No previous tag, get all commits
    CHANGELOG=$(git log --pretty=format:"- %s" --no-merges)
else
    # Get commits since last tag
    CHANGELOG=$(git log "${PREVIOUS_TAG}..HEAD" --pretty=format:"- %s" --no-merges)
fi

# If no changes, provide a default message
if [ -z "$CHANGELOG" ]; then
    CHANGELOG="- Version bump to $NEW_VERSION"
fi

# Generate release notes
RELEASE_NOTES="Release v$NEW_VERSION

## Changes
$CHANGELOG

## Installation
Download the appropriate package for your platform:
- **Windows**: \`TvnomsProxy-Setup-$NEW_VERSION.exe\` (auto-updating installer)
- **Linux (Debian/Ubuntu)**: \`tvnoms-proxy_${NEW_VERSION}_amd64.deb\`
- **Linux (Fedora/RHEL)**: \`tvnoms-proxy-${NEW_VERSION}-1.x86_64.rpm\`
- **Linux (Arch)**: \`tvnoms-proxy-${NEW_VERSION}-1-x86_64.pkg.tar.zst\`

See the included documentation for installation instructions."

# Create the release (this will trigger the GitHub Action workflow)
gh release create "v$NEW_VERSION" \
    --title "Release v$NEW_VERSION" \
    --notes "$RELEASE_NOTES" \
    --target "$CURRENT_BRANCH"

echo -e "${GREEN}✓ Release v$NEW_VERSION created successfully!${NC}"
echo -e "${GREEN}✓ GitHub Action workflow will build and attach installers automatically${NC}"
echo -e "${YELLOW}View release at: $(gh repo view --json url -q .url)/releases/tag/v$NEW_VERSION${NC}"


gh run watch --exit-status $(gh run list --status in_progress --limit 1 --json databaseId --jq '.[0].databaseId')