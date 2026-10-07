[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Module,
    [Parameter(Mandatory = $true)][string]$Name,
    [string]$Context = "TenancyDbContext",
    [string]$Project = "src/Modules/Tenancy/Tenancy.Infrastructure/Tenancy.Infrastructure.csproj",
    [string]$StartupProject = "src/Bootstrapper/Api/Api.csproj"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

dotnet ef migrations add $Name `
    --project $Project `
    --startup-project $StartupProject `
    --context $Context `
    --output-dir Persistence/Migrations

if ($LASTEXITCODE -ne 0) { throw "EF migration generation failed." }
