[CmdletBinding()]
param(
    [ValidateSet("initialize-all","provision")][string]$Command = "initialize-all",
    [Guid]$TenantId
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$args = @($Command)
if ($Command -eq "provision") {
    if ($TenantId -eq [Guid]::Empty) { throw "-TenantId is required when Command=provision." }
    $args += $TenantId.ToString()
}

dotnet run --project src/Hosts/Krafthaus.Migrator/Krafthaus.Migrator.csproj -- @args
if ($LASTEXITCODE -ne 0) { throw "Database migration/provisioning failed." }
