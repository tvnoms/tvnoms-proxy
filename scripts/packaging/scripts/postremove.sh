#!/bin/bash
# Post-remove for tvnoms-proxy (.deb / .rpm).
# Per-user state in ~/.config/tvnoms-proxy is intentionally left in place.

cat <<'EOF'

==> TV Noms Proxy removed.

Per-user state was left untouched. To remove it manually:
  rm -rf ~/.config/tvnoms-proxy

EOF
