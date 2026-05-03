using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using MaNoir.Core.Contracts.Models.Mesh;

namespace MaNoir.PlatformOps.Provider.Docker;

public sealed class DockerLocalCoreMeshSettingsClient : IDisposable
{
	private readonly HttpClient _httpClient;
	private readonly bool _ownsHttpClient;

	public DockerLocalCoreMeshSettingsClient(HttpClient httpClient = null)
	{
		if (httpClient == null)
		{
			_httpClient = new HttpClient();
			_ownsHttpClient = true;
		}
		else
		{
			_httpClient = httpClient;
		}
	}

	public static Uri ResolveCoreBaseUri(string configuredCoreBaseUri = null)
	{
		if (!string.IsNullOrWhiteSpace(configuredCoreBaseUri))
			return new Uri(configuredCoreBaseUri.Trim().TrimEnd('/') + "/", UriKind.Absolute);

		int hostPort = DockerCoreServiceCatalog.DefaultCoreAdminUiHostPort;
		string configuredHostPort = Environment.GetEnvironmentVariable(DockerCoreServiceCatalog.CoreAdminUiHostPortEnvironmentVariableName);
		if (!string.IsNullOrWhiteSpace(configuredHostPort) && int.TryParse(configuredHostPort.Trim(), out int parsedHostPort) && parsedHostPort > 0 && parsedHostPort <= 65535)
			hostPort = parsedHostPort;

		return new Uri("http://127.0.0.1:" + hostPort + "/", UriKind.Absolute);
	}

	public async Task<AutomationMeshLocalSettings> GetLocalSettingsAsync(string configuredCoreBaseUri = null, CancellationToken cancellationToken = default)
	{
		Uri requestUri = new Uri(ResolveCoreBaseUri(configuredCoreBaseUri), "api/core/system/mesh/local/settings");
		using HttpResponseMessage response = await _httpClient.GetAsync(requestUri, cancellationToken);
		if (response.StatusCode == HttpStatusCode.NotFound)
			return null;

		response.EnsureSuccessStatusCode();
		return await response.Content.ReadFromJsonAsync<AutomationMeshLocalSettings>(cancellationToken: cancellationToken);
	}

	public void Dispose()
	{
		if (_ownsHttpClient)
			_httpClient.Dispose();
	}
}