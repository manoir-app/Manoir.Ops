[CmdletBinding()]
param(
	[string]$ImageName = "manoir-agents-gaia",
	[string]$Tag = "local",
	[ValidateSet("Debug", "Release")]
	[string]$Configuration = "Release",
	[string]$Platform
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = "Stop"

$scriptDirectoryPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$repositoryRootPath = (Resolve-Path (Join-Path $scriptDirectoryPath "..")).Path
$dockerfilePath = Join-Path $repositoryRootPath "apps/MaNoir.PlatformOps.AdminUi/Dockerfile"
$adminUiProjectPath = Join-Path $repositoryRootPath "apps/MaNoir.PlatformOps.AdminUi/MaNoir.PlatformOps.AdminUi.csproj"
$imageReference = "$ImageName`:$Tag"

if (-not (Test-Path $dockerfilePath)) {
	throw "The Gaia Dockerfile was not found at '$dockerfilePath'."
}

if (-not (Test-Path $adminUiProjectPath)) {
	throw "The Gaia AdminUi project was not found at '$adminUiProjectPath'."
}

$dockerCommand = Get-Command docker -ErrorAction Stop
$dotnetCommand = Get-Command dotnet -ErrorAction Stop

Push-Location $repositoryRootPath
try {
	Write-Host "Building .NET project '$adminUiProjectPath'."
	& $dotnetCommand.Source build $adminUiProjectPath --configuration $Configuration
	if ($LASTEXITCODE -ne 0) {
		throw "dotnet build failed with exit code $LASTEXITCODE."
	}

	Write-Host "Building '$imageReference' from '$dockerfilePath'."
	$dockerBuildArgs = @(
		"build",
		"--file", $dockerfilePath,
		"--build-arg", "CONFIGURATION=$Configuration",
		"--tag", $imageReference
	)

	if (-not [string]::IsNullOrWhiteSpace($Platform)) {
		$dockerBuildArgs += @("--platform", $Platform.Trim())
	}

	$dockerBuildArgs += "."
	& $dockerCommand.Source @dockerBuildArgs
	if ($LASTEXITCODE -ne 0) {
		throw "docker build failed with exit code $LASTEXITCODE."
	}
}
finally {
	Pop-Location
}

Write-Host "Built image: $imageReference"
if (-not [string]::IsNullOrWhiteSpace($Platform)) {
	Write-Host "Platform: $($Platform.Trim())"
}