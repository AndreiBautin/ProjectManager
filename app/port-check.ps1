# Reports what is listening on a port, so start.bat can tell OUR app apart from
# some unrelated program that happens to have grabbed the same port.
#
# Prints exactly one line:
#   free                        nothing is listening
#   ours                        our own backend/frontend is already running
#   foreign|<name> (PID <pid>)  something else owns the port
#
# With -WaitSeconds N it keeps polling for up to N seconds for our app to answer,
# so the caller can wait for a starting server with a single PowerShell launch.
param(
    [Parameter(Mandatory = $true)][int]$Port,
    [Parameter(Mandatory = $true)][ValidateSet('backend', 'frontend')][string]$Kind,
    [int]$WaitSeconds = 0
)

$ErrorActionPreference = 'SilentlyContinue'
$ProgressPreference = 'SilentlyContinue'

# Vite binds IPv6 loopback only, Kestrel binds IPv4 loopback, and "localhost"
# resolves to just one of them - so every check has to consider both.
$hosts = @('127.0.0.1', '[::1]')

function Test-Listening {
    # Get-NetTCPConnection sees every bound address, whatever the address family.
    $listeners = @(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)
    if ($listeners.Count -gt 0) { return $true }

    # Fallback for the unlikely case that cmdlet is unavailable.
    foreach ($h in @('127.0.0.1', '::1')) {
        $client = New-Object Net.Sockets.TcpClient
        try {
            $client.Connect($h, $Port)
            return $true
        } catch {
        } finally {
            $client.Close()
        }
    }
    return $false
}

function Test-Ours {
    # Our backend answers the health probe; our frontend serves the app shell.
    if ($Kind -eq 'backend') {
        $path = '/api/health'
        $marker = 'personal-coo'
    } else {
        $path = '/'
        $marker = '<title>Personal COO</title>'
    }

    foreach ($h in $hosts) {
        try {
            $response = Invoke-WebRequest -Uri "http://${h}:$Port$path" -TimeoutSec 5 -UseBasicParsing
        } catch {
            continue
        }
        if (([string]$response.Content).Contains($marker)) { return $true }
    }
    return $false
}

function Test-OursByPath {
    # Fallback for a build of ours that predates /api/health: if the listening
    # executable lives inside this repo, it is our own process either way.
    # (Never matches the frontend, whose node.exe lives outside the repo.)
    $connection = @(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) |
        Select-Object -First 1
    if (-not $connection) { return $false }

    $process = Get-Process -Id $connection.OwningProcess -ErrorAction SilentlyContinue
    if (-not $process -or -not $process.Path) { return $false }

    return $process.Path.StartsWith($PSScriptRoot, [StringComparison]::OrdinalIgnoreCase)
}

function Get-OwnerDescription {
    $connection = @(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) |
        Select-Object -First 1
    if (-not $connection) { return 'an unidentified process' }

    $process = Get-Process -Id $connection.OwningProcess -ErrorAction SilentlyContinue
    if (-not $process) { return "PID $($connection.OwningProcess)" }

    return "$($process.ProcessName) (PID $($process.Id))"
}

function Get-State {
    if (-not (Test-Listening)) { return 'free' }
    if (Test-Ours) { return 'ours' }
    if (Test-OursByPath) { return 'ours' }
    return "foreign|$(Get-OwnerDescription)"
}

$deadline = (Get-Date).AddSeconds($WaitSeconds)
while ($true) {
    $state = Get-State
    if ($state -eq 'ours' -or (Get-Date) -ge $deadline) { break }
    Start-Sleep -Milliseconds 500
}

Write-Output $state
