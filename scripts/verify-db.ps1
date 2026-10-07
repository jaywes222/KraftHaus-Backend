[CmdletBinding()]
param(
    [string]$ConnectionString = $env:KRAFTHAUS_PLATFORM_CONNECTION
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    throw "Set KRAFTHAUS_PLATFORM_CONNECTION or pass -ConnectionString."
}
$env:KRAFTHAUS_PLATFORM_CONNECTION = $ConnectionString

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

dotnet ef migrations list `
    --project src/Modules/Tenancy/Tenancy.Infrastructure/Tenancy.Infrastructure.csproj `
    --startup-project src/Bootstrapper/Api/Api.csproj `
    --context TenancyDbContext

if ($LASTEXITCODE -ne 0) { throw "Unable to inspect EF migrations." }
Write-Host "Migration metadata inspected. Runtime migration drift is checked by DatabaseInitializer before deployment."
