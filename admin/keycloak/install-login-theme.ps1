# Puts the Recuro sign-in pages (admin/keycloak-themes/recuro) on a Keycloak that is already running in
# Docker, and points the recuro-portal client at them (RCU-PLT-001). Safe to run again after every pull.
#
# The realm is imported with IGNORE_EXISTING, so a Keycloak created before the theme existed never gets it
# or the client's login_theme from recuro-realm.json. This script adds both without recreating the
# container (start-dev keeps its data inside it) and without touching anything else in the realm.
#
#   powershell -ExecutionPolicy Bypass -File admin\keycloak\install-login-theme.ps1 [-Container <name>]
param(
    [string]$Container = '',
    [string]$AdminUser = $(if ($env:KEYCLOAK_ADMIN_USER) { $env:KEYCLOAK_ADMIN_USER } else { 'admin' }),
    [string]$AdminPassword = $(if ($env:KEYCLOAK_ADMIN_PASSWORD) { $env:KEYCLOAK_ADMIN_PASSWORD } else { 'admin' })
)
$ErrorActionPreference = 'Stop'

$theme = Join-Path $PSScriptRoot '..\keycloak-themes\recuro'

if (-not $Container) {
    $Container = docker ps --format '{{.Names}} {{.Image}}' |
        Where-Object { ($_ -split ' ')[1] -match 'keycloak' } |
        ForEach-Object { ($_ -split ' ')[0] } |
        Select-Object -First 1
}
if (-not $Container) { throw 'No running Keycloak container found. Pass its name with -Container.' }
Write-Host "Keycloak container: $Container"

function Invoke-Docker {
    docker @args
    if ($LASTEXITCODE -ne 0) { throw "docker $($args -join ' ') failed" }
}

# Compose files from PR #61 on mount the themes folder read-only; then the files are already current.
$mounts = docker inspect -f '{{range .Mounts}}{{println .Destination}}{{end}}' $Container
if ($mounts -contains '/opt/keycloak/themes') {
    Write-Host 'Themes folder is mounted from the repo: nothing to copy.'
} else {
    Invoke-Docker exec $Container mkdir -p /opt/keycloak/themes/recuro
    Invoke-Docker cp "$theme\." "${Container}:/opt/keycloak/themes/recuro"
    Write-Host 'Copied the Recuro theme into the container.'
}

$config = '--config', '/tmp/recuro-kcadm.config'
Invoke-Docker exec $Container /opt/keycloak/bin/kcadm.sh config credentials --server http://localhost:8080 --realm master --user $AdminUser --password $AdminPassword @config | Out-Null
$id = (docker exec $Container /opt/keycloak/bin/kcadm.sh get clients -r recuro -q clientId=recuro-portal --fields id --format csv --noquotes @config | Out-String).Trim()
if (-not $id) { throw 'The recuro realm has no recuro-portal client.' }
# Only the login_theme attribute changes; kcadm keeps the rest of the client as it is.
Invoke-Docker exec $Container /opt/keycloak/bin/kcadm.sh update "clients/$id" -r recuro -s 'attributes.login_theme=recuro' @config
Invoke-Docker exec $Container rm -f /tmp/recuro-kcadm.config
Write-Host 'recuro-portal now uses the Recuro login theme. Sign in from the portal to see it.'
