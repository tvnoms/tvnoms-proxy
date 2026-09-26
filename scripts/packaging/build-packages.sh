#!/usr/bin/env bash
set -e

# Build packages using nfpm
# This script assumes:
# 1. The application has been built and published to ../../publish/linux/
# 2. nfpm is installed (https://nfpm.goreleaser.com/install/)

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
PACKAGING_DIR="$SCRIPT_DIR"
OUTPUT_DIR="$PROJECT_ROOT/publish/packages"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Function to print colored output
print_info() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Check if nfpm is installed
if ! command -v nfpm &> /dev/null; then
    print_error "nfpm is not installed!"
    echo ""
    echo "To install nfpm, visit: https://nfpm.goreleaser.com/install/"
    echo ""
    echo "Quick install options:"
    echo "  # Using Go:"
    echo "  go install github.com/goreleaser/nfpm/v2/cmd/nfpm@latest"
    echo ""
    echo "  # Using Homebrew (macOS/Linux):"
    echo "  brew install nfpm"
    echo ""
    echo "  # Using apt (Debian/Ubuntu):"
    echo "  echo 'deb [trusted=yes] https://repo.goreleaser.com/apt/ /' | sudo tee /etc/apt/sources.list.d/goreleaser.list"
    echo "  sudo apt update"
    echo "  sudo apt install nfpm"
    echo ""
    exit 1
fi

# Check if the application has been published
if [ ! -f "$PROJECT_ROOT/publish/linux/tvnoms-proxy" ]; then
    print_error "Application not found at $PROJECT_ROOT/publish/linux/tvnoms-proxy"
    print_info "Please run the build script first: ./scripts/build.sh"
    exit 1
fi

# Get version from environment variable or csproj file
if [ -n "$VERSION" ]; then
    # Remove 'v' prefix if present (e.g., v1.3.1 -> 1.3.1)
    VERSION="${VERSION#v}"
    print_info "Using version from environment: $VERSION"
else
    VERSION=$(grep -oP '<Version>\K[^<]+' "$PROJECT_ROOT/proxy/tvnoms-proxy.csproj" | head -1)
    if [ -z "$VERSION" ]; then
        print_warning "Could not extract version from .csproj file, using 0.0.0"
        VERSION="0.0.0"
    fi
    print_info "Extracted version from .csproj: $VERSION"
fi

print_info "Building packages for version: $VERSION"

# Create output directory
mkdir -p "$OUTPUT_DIR"

# Export version for nfpm
export VERSION

# Change to packaging directory
cd "$PACKAGING_DIR"

# Make scripts executable
chmod +x scripts/*.sh

# Build DEB package
print_info "Building DEB package..."
if nfpm package \
    --config nfpm.yaml \
    --packager deb \
    --target "$OUTPUT_DIR/tvnoms-proxy_${VERSION}_amd64.deb"; then
    print_info "DEB package created: $OUTPUT_DIR/tvnoms-proxy_${VERSION}_amd64.deb"
else
    print_error "Failed to build DEB package"
    exit 1
fi

# Build RPM package
print_info "Building RPM package..."
if nfpm package \
    --config nfpm.yaml \
    --packager rpm \
    --target "$OUTPUT_DIR/tvnoms-proxy-${VERSION}-1.x86_64.rpm"; then
    print_info "RPM package created: $OUTPUT_DIR/tvnoms-proxy-${VERSION}-1.x86_64.rpm"
else
    print_error "Failed to build RPM package"
    exit 1
fi

# Build Arch Linux package
print_info "Building Arch Linux package..."
if nfpm package \
    --config nfpm.yaml \
    --packager archlinux \
    --target "$OUTPUT_DIR/tvnoms-proxy-${VERSION}-1-x86_64.pkg.tar.zst"; then
    print_info "Arch Linux package created: $OUTPUT_DIR/tvnoms-proxy-${VERSION}-1-x86_64.pkg.tar.zst"
else
    print_error "Failed to build Arch Linux package"
    exit 1
fi

print_info "All packages built successfully!"
print_info "Packages are located in: $OUTPUT_DIR"
echo ""
ls -lh "$OUTPUT_DIR"
echo ""

# Print installation instructions
echo "==================== Installation Instructions ===================="
echo ""
echo "Debian/Ubuntu:"
echo "  sudo dpkg -i $OUTPUT_DIR/tvnoms-proxy_${VERSION}_amd64.deb"
echo "  sudo apt-get install -f  # Install dependencies if needed"
echo ""
echo "RHEL/Fedora/CentOS:"
echo "  sudo rpm -i $OUTPUT_DIR/tvnoms-proxy-${VERSION}-1.x86_64.rpm"
echo "  # or"
echo "  sudo dnf install $OUTPUT_DIR/tvnoms-proxy-${VERSION}-1.x86_64.rpm"
echo ""
echo "Arch Linux:"
echo "  sudo pacman -U $OUTPUT_DIR/tvnoms-proxy-${VERSION}-1-x86_64.pkg.tar.zst"
echo ""
echo "===================================================================="
