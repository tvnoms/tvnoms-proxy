#!/usr/bin/env bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

print_info() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Get version from environment or parameter
VERSION="${VERSION:-$1}"
if [ -z "$VERSION" ]; then
    print_error "VERSION not set. Usage: VERSION=1.0.0 $0 or $0 1.0.0"
    exit 1
fi

# Remove 'v' prefix if present
VERSION="${VERSION#v}"

print_info "Pushing version $VERSION to AUR..."

# Temporary directory for AUR repo
AUR_DIR=$(mktemp -d)
trap "rm -rf $AUR_DIR" EXIT

# Clone the AUR repository
print_info "Cloning AUR repository..."
cd "$AUR_DIR"

# Set GIT_SSH_COMMAND to use BatchMode (prevents hanging on interactive prompts)
export GIT_SSH_COMMAND="${GIT_SSH_COMMAND:-ssh -o BatchMode=yes -o StrictHostKeyChecking=no}"

if ! git clone ssh://aur@aur.archlinux.org/tvnoms-proxy.git; then
    print_error "Failed to clone AUR repository"
    print_error "Make sure your SSH key is configured for AUR access"
    exit 1
fi

cd tvnoms-proxy

# Update PKGBUILD with new version
print_info "Updating PKGBUILD..."
sed "s/VERSION_PLACEHOLDER/$VERSION/g" "$SCRIPT_DIR/PKGBUILD.template" > PKGBUILD

# Copy the install script
print_info "Copying install script..."
cp "$SCRIPT_DIR/tvnoms-proxy.install" .

# Update .SRCINFO
print_info "Generating .SRCINFO..."
if ! command -v makepkg &> /dev/null; then
    print_warning "makepkg not found, attempting to generate .SRCINFO manually"
    # Generate basic .SRCINFO
    cat > .SRCINFO << EOF
pkgbase = tvnoms-proxy
	pkgdesc = TV Noms Proxy Service
	pkgver = $VERSION
	pkgrel = 1
	url = https://github.com/tvnoms/tvnoms-proxy
	arch = x86_64
	license = MIT
	depends = glibc
	provides = tvnoms-proxy
	conflicts = tvnoms-proxy
	backup = etc/tvnoms-proxy/appsettings.json
	source = https://github.com/tvnoms/tvnoms-proxy/releases/download/v$VERSION/tvnoms-proxy-linux.tar.gz
	sha256sums = SKIP

pkgname = tvnoms-proxy
EOF
else
    makepkg --printsrcinfo > .SRCINFO
fi

# Configure git
print_info "Configuring git..."
git config user.name "GitHub Actions"
git config user.email "actions@github.com"

# Check if there are changes
if git diff --quiet && git diff --cached --quiet; then
    print_info "No changes to commit, AUR is already up to date"
    exit 0
fi

# Commit and push
print_info "Committing changes..."
git add PKGBUILD .SRCINFO tvnoms-proxy.install
git commit -m "Update to version $VERSION"

print_info "Pushing to AUR..."
if git push; then
    print_info "Successfully pushed version $VERSION to AUR!"
else
    print_error "Failed to push to AUR"
    exit 1
fi

print_info "AUR package updated successfully!"

