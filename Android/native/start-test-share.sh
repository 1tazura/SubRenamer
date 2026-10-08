#!/usr/bin/env bash
# CI-only loopback fixture. Never run against a real media directory.
set -euo pipefail
root="${1:?private fixture root required}"
mkdir -p "$root"/{fixture,lock,state,cache,pid,private}
root="$(cd "$root" && pwd)"
# Root guest is restricted to this loopback-only, disposable test share.
# App/client tests run as the ordinary unprivileged runner.
cat > "$root/smb.conf" <<EOF
[global]
server role = standalone server
interfaces = 127.0.0.1
bind interfaces only = yes
smb ports = 445
server min protocol = SMB2
security = user
map to guest = Bad User
guest account = root
log file = $root/log.%m
lock directory = $root/lock
state directory = $root/state
cache directory = $root/cache
pid directory = $root/pid
private dir = $root/private
load printers = no
disable spoolss = yes
[Fixture]
path = $root/fixture
guest ok = yes
guest only = yes
read only = no
follow symlinks = no
EOF
sudo smbd -D -s "$root/smb.conf"
