using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MaNoir.Core.Secrets;

namespace MaNoir.PlatformOps.Core;

public static class PlatformOpsSharedSecretNames
{
	public const string OvhDnsApplicationKey = "PLATFORMOPS_OVH_DNS_APPLICATION_KEY";

	public const string OvhDnsApplicationSecret = "PLATFORMOPS_OVH_DNS_APPLICATION_SECRET";

	public const string OvhDnsConsumerKey = "PLATFORMOPS_OVH_DNS_CONSUMER_KEY";
}

public sealed class OvhDnsChallengeSecretConfiguration
{
	public string ZoneName { get; set; }

	public string ApplicationKeySecretName { get; set; } = PlatformOpsSharedSecretNames.OvhDnsApplicationKey;

	public string ApplicationSecretSecretName { get; set; } = PlatformOpsSharedSecretNames.OvhDnsApplicationSecret;

	public string ConsumerKeySecretName { get; set; } = PlatformOpsSharedSecretNames.OvhDnsConsumerKey;

	public string ApiBaseUrl { get; set; } = "https://eu.api.ovh.com/1.0";

	public int RecordTtl { get; set; } = 360;
}

public sealed class OvhDnsChallengeCredentials
{
	public string ZoneName { get; set; }

	public string ApplicationKey { get; set; }

	public string ApplicationSecret { get; set; }

	public string ConsumerKey { get; set; }

	public string ApiBaseUrl { get; set; }

	public int RecordTtl { get; set; }
}

public static class OvhDnsChallengeCredentialsResolver
{
	public static Task<OvhDnsChallengeCredentials> ResolveAsync(OvhDnsChallengeSecretConfiguration configuration, CancellationToken cancellationToken = default)
	{
		SharedSecretLogic sharedSecretLogic = new SharedSecretLogic();
		return ResolveAsync(configuration, sharedSecretLogic.GetSecretAsync, cancellationToken);
	}

	public static async Task<OvhDnsChallengeCredentials> ResolveAsync(
		OvhDnsChallengeSecretConfiguration configuration,
		Func<string, CancellationToken, Task<string>> resolveSecretAsync,
		CancellationToken cancellationToken = default)
	{
		if (configuration == null)
			throw new ArgumentNullException(nameof(configuration));

		if (resolveSecretAsync == null)
			throw new ArgumentNullException(nameof(resolveSecretAsync));

		List<string> errors = new List<string>();
		ValidateSecretConfiguration(configuration, errors);
		if (errors.Count > 0)
			throw new PlatformOpsSharedSecretsResolutionException(errors);

		string applicationKey = await resolveRequiredSecretAsync(resolveSecretAsync, configuration.ApplicationKeySecretName, "ApplicationKeySecretName", errors, cancellationToken);
		string applicationSecret = await resolveRequiredSecretAsync(resolveSecretAsync, configuration.ApplicationSecretSecretName, "ApplicationSecretSecretName", errors, cancellationToken);
		string consumerKey = await resolveRequiredSecretAsync(resolveSecretAsync, configuration.ConsumerKeySecretName, "ConsumerKeySecretName", errors, cancellationToken);

		if (errors.Count > 0)
			throw new PlatformOpsSharedSecretsResolutionException(errors);

		return new OvhDnsChallengeCredentials()
		{
			ZoneName = configuration.ZoneName.Trim().Trim('.'),
			ApplicationKey = applicationKey,
			ApplicationSecret = applicationSecret,
			ConsumerKey = consumerKey,
			ApiBaseUrl = configuration.ApiBaseUrl.Trim().TrimEnd('/'),
			RecordTtl = configuration.RecordTtl
		};
	}

	private static void ValidateSecretConfiguration(OvhDnsChallengeSecretConfiguration configuration, List<string> errors)
	{
		if (string.IsNullOrWhiteSpace(configuration.ZoneName))
			errors.Add("The OVH DNS zone name is required.");

		if (string.IsNullOrWhiteSpace(configuration.ApplicationKeySecretName))
			errors.Add("The OVH DNS application key secret name is required.");

		if (string.IsNullOrWhiteSpace(configuration.ApplicationSecretSecretName))
			errors.Add("The OVH DNS application secret secret name is required.");

		if (string.IsNullOrWhiteSpace(configuration.ConsumerKeySecretName))
			errors.Add("The OVH DNS consumer key secret name is required.");

		if (string.IsNullOrWhiteSpace(configuration.ApiBaseUrl))
			errors.Add("The OVH DNS API base URL is required.");

		if (configuration.RecordTtl <= 0)
			errors.Add("The OVH DNS TXT record TTL must be greater than zero.");
	}

	private static async Task<string> resolveRequiredSecretAsync(
		Func<string, CancellationToken, Task<string>> resolveSecretAsync,
		string secretName,
		string propertyName,
		List<string> errors,
		CancellationToken cancellationToken)
	{
		string secretValue = await resolveSecretAsync(secretName, cancellationToken);
		if (string.IsNullOrWhiteSpace(secretValue))
		{
			errors.Add("The shared secret '" + secretName + "' referenced by '" + propertyName + "' was not found.");
			return null;
		}

		return secretValue;
	}
}

public sealed class OvhDnsTxtChallengeWriter : IAcmeDnsTxtChallengeWriter, IDisposable
{
	private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

	private readonly OvhDnsChallengeCredentials _credentials;
	private readonly HttpClient _httpClient;
	private readonly bool _ownsHttpClient;

	public OvhDnsTxtChallengeWriter(OvhDnsChallengeCredentials credentials, HttpClient httpClient = null)
	{
		_credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));

		if (string.IsNullOrWhiteSpace(_credentials.ZoneName))
			throw new ArgumentException("The OVH DNS zone name is required.", nameof(credentials));

		if (string.IsNullOrWhiteSpace(_credentials.ApplicationKey))
			throw new ArgumentException("The OVH DNS application key is required.", nameof(credentials));

		if (string.IsNullOrWhiteSpace(_credentials.ApplicationSecret))
			throw new ArgumentException("The OVH DNS application secret is required.", nameof(credentials));

		if (string.IsNullOrWhiteSpace(_credentials.ConsumerKey))
			throw new ArgumentException("The OVH DNS consumer key is required.", nameof(credentials));

		if (string.IsNullOrWhiteSpace(_credentials.ApiBaseUrl))
			throw new ArgumentException("The OVH DNS API base URL is required.", nameof(credentials));

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

	public static string GetRelativeRecordName(string fullyQualifiedRecordName, string zoneName)
	{
		if (string.IsNullOrWhiteSpace(fullyQualifiedRecordName))
			throw new ArgumentException("A fully qualified DNS record name is required.", nameof(fullyQualifiedRecordName));

		if (string.IsNullOrWhiteSpace(zoneName))
			throw new ArgumentException("A DNS zone name is required.", nameof(zoneName));

		string normalizedRecordName = fullyQualifiedRecordName.Trim().Trim('.');
		string normalizedZoneName = zoneName.Trim().Trim('.');
		string zoneSuffix = "." + normalizedZoneName;

		if (string.Equals(normalizedRecordName, normalizedZoneName, StringComparison.OrdinalIgnoreCase))
			return string.Empty;

		if (!normalizedRecordName.EndsWith(zoneSuffix, StringComparison.OrdinalIgnoreCase))
			throw new ArgumentException("The DNS record '" + normalizedRecordName + "' does not belong to the zone '" + normalizedZoneName + "'.", nameof(fullyQualifiedRecordName));

		return normalizedRecordName.Substring(0, normalizedRecordName.Length - zoneSuffix.Length);
	}

	public async Task UpsertTxtRecordAsync(string fullyQualifiedRecordName, string value, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(value))
			throw new ArgumentException("A TXT record value is required.", nameof(value));

		string relativeRecordName = GetRelativeRecordName(fullyQualifiedRecordName, _credentials.ZoneName);
		string encodedRecordName = Uri.EscapeDataString(relativeRecordName);

		long[] existingRecordIds = await SendAsync<long[]>(HttpMethod.Get, "/domain/zone/" + _credentials.ZoneName + "/record?fieldType=TXT&subDomain=" + encodedRecordName, null, cancellationToken)
			?? Array.Empty<long>();

		if (existingRecordIds.Length == 0)
		{
			await SendAsync<OvhCreateRecordResponse>(
				HttpMethod.Post,
				"/domain/zone/" + _credentials.ZoneName + "/record",
				new OvhCreateRecordRequest()
				{
					Target = value,
					Ttl = _credentials.RecordTtl,
					FieldType = "TXT",
					SubDomain = relativeRecordName
				},
				cancellationToken);
		}
		else
		{
			await SendAsync<string>(
				HttpMethod.Put,
				"/domain/zone/" + _credentials.ZoneName + "/record/" + existingRecordIds[0],
				new OvhUpdateRecordRequest() { Target = value },
				cancellationToken);
		}

		await SendAsync<string>(HttpMethod.Post, "/domain/zone/" + _credentials.ZoneName + "/refresh", null, cancellationToken);
	}

	public void Dispose()
	{
		if (_ownsHttpClient)
			_httpClient.Dispose();
	}

	private async Task<T> SendAsync<T>(HttpMethod method, string relativeUrl, object body, CancellationToken cancellationToken)
	{
		string absoluteUrl = BuildAbsoluteUrl(relativeUrl);
		string serializedBody = body == null ? string.Empty : JsonSerializer.Serialize(body, SerializerOptions);
		long unixTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		string signature = "$1$" + ComputeSha1Hex(
			_credentials.ApplicationSecret + "+"
			+ _credentials.ConsumerKey + "+"
			+ method.Method.ToUpperInvariant() + "+"
			+ absoluteUrl + "+"
			+ serializedBody + "+"
			+ unixTimestamp.ToString());

		using HttpRequestMessage request = new HttpRequestMessage(method, absoluteUrl);
		request.Headers.Add("X-Ovh-Application", _credentials.ApplicationKey);
		request.Headers.Add("X-Ovh-Consumer", _credentials.ConsumerKey);
		request.Headers.Add("X-Ovh-Signature", signature);
		request.Headers.Add("X-Ovh-Timestamp", unixTimestamp.ToString());

		if (body != null)
			request.Content = new StringContent(serializedBody, Encoding.UTF8, "application/json");

		using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
		string responsePayload = await response.Content.ReadAsStringAsync(cancellationToken);

		if (!response.IsSuccessStatusCode)
			throw new InvalidOperationException("The OVH API request failed with status '" + (int)response.StatusCode + "': " + responsePayload);

		if (typeof(T) == typeof(string))
			return (T)(object)responsePayload;

		if (string.IsNullOrWhiteSpace(responsePayload))
			return default;

		return JsonSerializer.Deserialize<T>(responsePayload, SerializerOptions);
	}

	private string BuildAbsoluteUrl(string relativeUrl)
	{
		return _credentials.ApiBaseUrl.TrimEnd('/') + "/" + relativeUrl.TrimStart('/');
	}

	private static string ComputeSha1Hex(string value)
	{
		using SHA1 sha1 = SHA1.Create();
		byte[] hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(value));
		StringBuilder builder = new StringBuilder(hash.Length * 2);

		for (int index = 0; index < hash.Length; index++)
			builder.Append(hash[index].ToString("x2"));

		return builder.ToString();
	}

	private sealed class OvhCreateRecordRequest
	{
		[JsonPropertyName("fieldType")]
		public string FieldType { get; set; }

		[JsonPropertyName("subDomain")]
		public string SubDomain { get; set; }

		[JsonPropertyName("target")]
		public string Target { get; set; }

		[JsonPropertyName("ttl")]
		public int Ttl { get; set; }
	}

	private sealed class OvhUpdateRecordRequest
	{
		[JsonPropertyName("target")]
		public string Target { get; set; }
	}

	private sealed class OvhCreateRecordResponse
	{
		[JsonPropertyName("id")]
		public long Id { get; set; }
	}
}