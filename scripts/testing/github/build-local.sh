#!/usr/bin/env bash
set -e

# Run GitHub Actions workflow locally using act
# This script uses act to execute .github/workflows/build-installers.yaml locally

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"  # repo root (holds .github/)

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Function to print colored output
print_info() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

print_step() {
    echo -e "${BLUE}[STEP]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Default values
JOB="build-linux"
VERSION=""
WORKFLOW="build-installers.yaml"
DRY_RUN=false
VERBOSE=false
LIST_JOBS=false
CLEAR_CACHE=false
PLATFORM=""

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -j|--job)
            JOB="$2"
            shift 2
            ;;
        -v|--version)
            VERSION="$2"
            shift 2
            ;;
        -w|--workflow)
            WORKFLOW="$2"
            shift 2
            ;;
        -l|--list)
            LIST_JOBS=true
            shift
            ;;
        --clear-cache)
            CLEAR_CACHE=true
            shift
            ;;
        -n|--dry-run)
            DRY_RUN=true
            shift
            ;;
        --verbose)
            VERBOSE=true
            shift
            ;;
        -p|--platform)
            PLATFORM="$2"
            shift 2
            ;;
        -h|--help)
            echo "Usage: $0 [OPTIONS]"
            echo ""
            echo "Run GitHub Actions workflows locally using act"
            echo ""
            echo "Options:"
            echo "  -j, --job JOB              Run specific job (default: build-linux)"
            echo "  -v, --version VERSION      Set version tag (e.g., v1.4.16)"
            echo "  -w, --workflow WORKFLOW    Workflow file to run (default: build-installers.yaml)"
            echo "  -l, --list                 List available jobs in the workflow"
            echo "  -n, --dry-run              Show what would be run without executing"
            echo "  -p, --platform PLATFORM    Override container platform (e.g., ubuntu-latest=catthehacker/ubuntu:act-latest)"
            echo "      --clear-cache          Clear act containers and artifact cache before running"
            echo "      --verbose              Enable verbose output"
            echo "  -h, --help                 Show this help message"
            echo ""
            echo "Examples:"
            echo "  # Run the build-linux job"
            echo "  $0"
            echo ""
            echo "  # Run with a specific version tag"
            echo "  $0 --version v1.4.16"
            echo ""
            echo "  # Run the build-windows job"
            echo "  $0 --job build-windows"
            echo ""
            echo "  # List all jobs in the workflow"
            echo "  $0 --list"
            echo ""
            echo "  # Dry run to see what would execute"
            echo "  $0 --dry-run"
            echo ""
            echo "  # Use a specific Docker image"
            echo "  $0 --platform ubuntu-latest=catthehacker/ubuntu:act-latest"
            echo ""
            exit 0
            ;;
        *)
            print_error "Unknown option: $1"
            echo "Use -h or --help for usage information"
            exit 1
            ;;
    esac
done

# Change to project root
cd "$PROJECT_ROOT"

# Check if act is installed
if ! command -v act &> /dev/null; then
    print_error "act is not installed!"
    echo ""
    echo "To install act, visit: https://github.com/nektos/act"
    echo ""
    echo "Quick install options:"
    echo "  # Using Homebrew (macOS/Linux):"
    echo "  brew install act"
    echo ""
    echo "  # Using AUR (Arch Linux):"
    echo "  yay -S act"
    echo ""
    echo "  # Using curl (Linux):"
    echo "  curl -s https://raw.githubusercontent.com/nektos/act/master/install.sh | sudo bash"
    echo ""
    exit 1
fi

print_info "Using act version: $(act --version)"
echo ""

# List jobs if requested
if [ "$LIST_JOBS" = true ]; then
    print_step "Listing jobs in workflow: $WORKFLOW"
    act -l -W ".github/workflows/$WORKFLOW"
    exit 0
fi

# Clear cache if requested
if [ "$CLEAR_CACHE" = true ]; then
    print_step "Clearing act cache..."
    
    # Remove act containers
    print_info "Removing act containers..."
    ACT_CONTAINERS=$(docker ps -a --filter "label=act" --format "{{.ID}}" 2>/dev/null || true)
    if [ -n "$ACT_CONTAINERS" ]; then
        echo "$ACT_CONTAINERS" | xargs docker rm -f 2>/dev/null || true
        print_info "Removed act containers"
    else
        print_info "No act containers found"
    fi
    
    # Clear artifact directory
    if [ -d "/tmp/act-artifacts" ]; then
        print_info "Clearing artifact directory..."
        rm -rf /tmp/act-artifacts/*
        print_info "Artifact directory cleared"
    fi
    
    print_info "Cache cleared successfully!"
    echo ""
fi

# Get version from csproj if not provided
if [ -z "$VERSION" ]; then
    VERSION=$(grep -oP '<Version>\K[^<]+' "$PROJECT_ROOT/proxy/tvnoms-proxy.csproj" | head -1)
    if [ -z "$VERSION" ]; then
        print_warning "Could not extract version from .csproj file, using v0.0.0-local"
        VERSION="v0.0.0-local"
    else
        VERSION="v$VERSION"
    fi
    print_info "Auto-detected version: $VERSION"
else
    print_info "Using version: $VERSION"
fi

# Check for AUR SSH key and read it securely (never write to disk)
AUR_SSH_KEY=""
if [ -f "$HOME/.ssh/aur" ]; then
    print_info "Found AUR SSH key at $HOME/.ssh/aur"
    # Read the key into memory only - never write it to a file
    AUR_SSH_KEY=$(cat "$HOME/.ssh/aur")
else
    print_warning "AUR SSH key not found at $HOME/.ssh/aur"
    print_warning "The 'Push to AUR' step will be skipped or may fail"
fi

# Build act command
ACT_CMD="act"

ACT_CMD="$ACT_CMD -P ubuntu-latest=catthehacker/ubuntu:act-latest"
ACT_CMD="$ACT_CMD --container-architecture=linux/amd64"

# Enable container reuse for faster subsequent runs
ACT_CMD="$ACT_CMD --reuse"

# Use bind mounts for better performance and caching
ACT_CMD="$ACT_CMD --use-gitignore=false"

# Add job filter
if [ -n "$JOB" ]; then
    ACT_CMD="$ACT_CMD -j $JOB"
fi

# Add workflow file
ACT_CMD="$ACT_CMD -W .github/workflows/$WORKFLOW"

# Add event type (simulating a release event)
ACT_CMD="$ACT_CMD release"

# Add event payload with version
ACT_CMD="$ACT_CMD -e <(echo '{\"action\": \"published\", \"release\": {\"tag_name\": \"$VERSION\", \"name\": \"$VERSION\"}}')"

# Add secrets securely (passed via environment, never written to disk)
if [ -n "$AUR_SSH_KEY" ]; then
    ACT_CMD="$ACT_CMD -s AUR_SSH_KEY"
    export AUR_SSH_KEY
fi

# Add platform override if specified
if [ -n "$PLATFORM" ]; then
    ACT_CMD="$ACT_CMD -P $PLATFORM"
fi

# Add verbose flag if requested
if [ "$VERBOSE" = true ]; then
    ACT_CMD="$ACT_CMD -v"
fi

# Add dry-run flag if requested
if [ "$DRY_RUN" = true ]; then
    ACT_CMD="$ACT_CMD -n"
fi

# Add artifact server (enables artifact upload/download)
ACT_CMD="$ACT_CMD --artifact-server-path /tmp/act-artifacts"

# Print what we're doing
echo "========================================================"
print_step "Running GitHub Actions workflow locally"
echo "========================================================"
echo ""
echo "  Workflow: $WORKFLOW"
echo "  Job:      $JOB"
echo "  Version:  $VERSION"
echo "  Event:    release (published)"
echo ""

if [ "$DRY_RUN" = true ]; then
    print_warning "DRY RUN MODE - No actions will be executed"
    echo ""
fi

echo "========================================================"
echo ""

# Run act
print_info "Executing act..."
echo ""

# Use eval to properly handle process substitution
eval "$ACT_CMD"

EXIT_CODE=$?

echo ""
echo "========================================================"

if [ $EXIT_CODE -eq 0 ]; then
    print_info "Workflow completed successfully!"
    echo ""
    
    if [ "$DRY_RUN" = false ]; then
        print_info "Artifacts location: /tmp/act-artifacts"
        echo ""
        
        if [ -d "/tmp/act-artifacts" ] && [ "$(ls -A /tmp/act-artifacts 2>/dev/null)" ]; then
            echo "Generated artifacts:"
            ls -lh /tmp/act-artifacts/
        fi
    fi
else
    print_error "Workflow failed with exit code $EXIT_CODE"
    echo ""
fi

echo "========================================================"

exit $EXIT_CODE

