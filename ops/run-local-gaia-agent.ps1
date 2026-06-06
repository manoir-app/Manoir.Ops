[CmdletBinding()]
param(
	[string]$ImageName = "manoir-agents-gaia",
	[string]$Tag = "local",
	[string]$ContainerName = "manoir-agents-gaia",
	[int]$WebPort = 5056,
	[int]$CoreAdminUiHostPort = 81,
	[string]$ApiKey,
	[string]$SecretsSaltBase64,
	[string]$AuthJwtSigningKey,
	[string]$MongoImage,
	[string]$SharedServicesRootPath,
	[string]$DockerSocketSource,
	[string[]]$PluginsRepo,
	[int]$EnsureIntervalSeconds = 300,
	[switch]$ProductionInstance
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = "Stop"

function New-RandomBase64String {
	param([int]$ByteCount = 32)

	$bytes = New-Object byte[] $ByteCount
	[System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
	return [Convert]::ToBase64String($bytes)
}

function Resolve-DockerSocketSource {
	param(
		[string]$RequestedSource,
		[string]$DockerServerOs,
		[string]$HostOperatingSystem
	)

	if (-not [string]::Equals($DockerServerOs, "linux", [System.StringComparison]::OrdinalIgnoreCase)) {
		throw "The Gaia image is built for a Linux container runtime. Current Docker server OS: '$DockerServerOs'."
	}

	if ([string]::IsNullOrWhiteSpace($RequestedSource)) {
		return "/var/run/docker.sock"
	}

	if ([string]::Equals($HostOperatingSystem, "windows", [System.StringComparison]::OrdinalIgnoreCase) -and $RequestedSource.StartsWith('\\.\pipe\', [System.StringComparison]::OrdinalIgnoreCase)) {
		throw "Named pipes are not supported for this Gaia Linux container. Use '/var/run/docker.sock' with Docker Desktop Linux containers."
	}

	return $RequestedSource
}

function Resolve-DefaultHomeAutomationRootPath {
	param([string]$HostOperatingSystem)

	if ([string]::Equals($HostOperatingSystem, "windows", [System.StringComparison]::OrdinalIgnoreCase)) {
		if ([string]::IsNullOrWhiteSpace($env:ProgramData)) {
			throw "The ProgramData environment variable is required on Windows to resolve the default home-automation root path."
		}

		return (Join-Path $env:ProgramData "MaNoir/home-automation")
	}

	if ([string]::Equals($HostOperatingSystem, "linux", [System.StringComparison]::OrdinalIgnoreCase)) {
		return "/srv/manoir/home-automation"
	}

	throw "Unsupported host operating system '$HostOperatingSystem'."
}

$dockerCommand = Get-Command docker -ErrorAction Stop

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
	$ApiKey = [Guid]::NewGuid().ToString("D")
}

if ([string]::IsNullOrWhiteSpace($SecretsSaltBase64)) {
	$SecretsSaltBase64 = New-RandomBase64String -ByteCount 32
}

if ([string]::IsNullOrWhiteSpace($AuthJwtSigningKey)) {
	$AuthJwtSigningKey = New-RandomBase64String -ByteCount 32
}

$hostOs = if ($IsWindows) { "windows" } elseif ($IsLinux) { "linux" } else { "unknown" }
if ($hostOs -eq "unknown") {
	throw "This script only supports PowerShell on Windows or Linux hosts."
}

if ([string]::IsNullOrWhiteSpace($SharedServicesRootPath)) {
	$SharedServicesRootPath = Join-Path (Resolve-DefaultHomeAutomationRootPath -HostOperatingSystem $hostOs) "shared-services"
}

$resolvedSharedServicesRootPath = [System.IO.Path]::GetFullPath($SharedServicesRootPath)
$resolvedHomeAutomationRootPath = Split-Path -Parent $resolvedSharedServicesRootPath
$resolvedPluginRepositoriesRootPath = Join-Path $resolvedHomeAutomationRootPath "plugins"
$sharedServicesContainerPath = "/home-automation/" + (Split-Path -Leaf $resolvedSharedServicesRootPath)
$pluginRepositoriesContainerPath = "/home-automation/plugins"
$mqttConfigPath = Join-Path $resolvedSharedServicesRootPath "mqtt/config"
$mqttDataPath = Join-Path $resolvedSharedServicesRootPath "mqtt/data"
$mqttLogPath = Join-Path $resolvedSharedServicesRootPath "mqtt/log"
$null = New-Item -ItemType Directory -Force -Path $resolvedHomeAutomationRootPath
$null = New-Item -ItemType Directory -Force -Path $resolvedSharedServicesRootPath
$null = New-Item -ItemType Directory -Force -Path $resolvedPluginRepositoriesRootPath
$null = New-Item -ItemType Directory -Force -Path $mqttConfigPath
$null = New-Item -ItemType Directory -Force -Path $mqttDataPath
$null = New-Item -ItemType Directory -Force -Path $mqttLogPath

$serverOs = (& $dockerCommand.Source version --format '{{.Server.Os}}').Trim()
if ($LASTEXITCODE -ne 0) {
	throw "Unable to query the Docker server OS."
}

$DockerSocketSource = Resolve-DockerSocketSource -RequestedSource $DockerSocketSource -DockerServerOs $serverOs -HostOperatingSystem $hostOs

& $dockerCommand.Source container inspect $ContainerName *> $null
if ($LASTEXITCODE -eq 0) {
	Write-Host "Removing existing container '$ContainerName'."
	& $dockerCommand.Source rm --force $ContainerName | Out-Null
	if ($LASTEXITCODE -ne 0) {
		throw "The existing container '$ContainerName' could not be removed."
	}
}

$imageReference = "$ImageName`:$Tag"
$env:HOMEAUTOMATION_APIKEY = $ApiKey
$env:HOMEAUTOMATION_SECRETS_SALT = $SecretsSaltBase64
$env:HOMEAUTOMATION_AUTH_JWT_SIGNING_KEY = $AuthJwtSigningKey

$dockerArgs = @(
	"run",
	"--detach",
	"--name", $ContainerName,
	"--restart", "unless-stopped",
	"--publish", "${WebPort}:8080",
	"--mount", "type=bind,source=$DockerSocketSource,target=/var/run/docker.sock",
	"--mount", "type=bind,source=$resolvedHomeAutomationRootPath,target=/home-automation",
	"--env", "ASPNETCORE_URLS=http://0.0.0.0:8080",
	"--env", "DOCKER_HOST=unix:///var/run/docker.sock",
	"--env", "HOMEAUTOMATION_APIKEY=$ApiKey",
	"--env", "HOMEAUTOMATION_SECRETS_SALT=$SecretsSaltBase64",
	"--env", "HOMEAUTOMATION_AUTH_JWT_SIGNING_KEY=$AuthJwtSigningKey",
	"--env", "MANOIR_CORE_ADMINUI_HOST_PORT=$CoreAdminUiHostPort",
	"--env", "MANOIR_MONGO_IMAGE=$MongoImage",
	"--env", "MANOIR_SHARED_SERVICES_HOST_ROOT_PATH=$resolvedSharedServicesRootPath",
	"--env", "Gaia__SharedServicesRootPath=$sharedServicesContainerPath",
	"--env", "Gaia__PluginRepositoriesRootPath=$pluginRepositoriesContainerPath",
	"--env", "Gaia__EnsureIntervalSeconds=$EnsureIntervalSeconds",
	"--env", "Gaia__AutoEnsureSharedServicesOnStartup=true"
)


$isDevelopmentInstance = -not $ProductionInstance.IsPresent
if ($isDevelopmentInstance) {
	$dockerArgs += @("--env", "MANOIR_DEVELOPMENT_INSTANCE=true")
}

if ($PluginsRepo -and $PluginsRepo.Count -gt 0) {
	$pluginsRepoValue = (($PluginsRepo | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join ',')
	if (-not [string]::IsNullOrWhiteSpace($pluginsRepoValue)) {
		$dockerArgs += @("--env", "MANOIR_PLUGINS_REPO=$pluginsRepoValue")
	}
}

$dockerArgs += $imageReference

Write-Host "Starting '$ContainerName' from '$imageReference' on host OS '$hostOs'."
$dockerRunOutput = & $dockerCommand.Source @dockerArgs
if ($LASTEXITCODE -ne 0) {
	throw "docker run failed with exit code $LASTEXITCODE."
}

$containerId = ($dockerRunOutput | Select-Object -Last 1)
if (-not [string]::IsNullOrWhiteSpace($containerId)) {
	$containerId = $containerId.Trim()
}

Write-Host "ContainerId: $containerId"
Write-Host "Web UI: http://127.0.0.1:$WebPort"
Write-Host "HOMEAUTOMATION_APIKEY=$ApiKey"
Write-Host "HOMEAUTOMATION_SECRETS_SALT=$SecretsSaltBase64"
Write-Host "HOMEAUTOMATION_AUTH_JWT_SIGNING_KEY length=$($AuthJwtSigningKey.Length)"
Write-Host "MANOIR_CORE_ADMINUI_HOST_PORT=$CoreAdminUiHostPort"
Write-Host "MANOIR_MONGO_IMAGE=$MongoImage"
Write-Host "MANOIR_SHARED_SERVICES_HOST_ROOT_PATH=$resolvedSharedServicesRootPath"
Write-Host "Home automation root: $resolvedHomeAutomationRootPath"
Write-Host "Shared services root: $resolvedSharedServicesRootPath"
Write-Host "Gaia shared services path: $sharedServicesContainerPath"
Write-Host "Plugin repositories root: $resolvedPluginRepositoriesRootPath"
Write-Host "Gaia plugin repositories path: $pluginRepositoriesContainerPath"
Write-Host "MANOIR_PLUGINS_REPO=$($PluginsRepo -join ',')"
Write-Host "Development instance: $isDevelopmentInstance"
Write-Host "Docker socket source: $DockerSocketSource"