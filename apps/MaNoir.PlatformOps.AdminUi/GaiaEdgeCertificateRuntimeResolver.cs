using System;
using System.Threading;
using System.Threading.Tasks;
using MaNoir.Core.Users;

namespace MaNoir.PlatformOps.AdminUi;

internal sealed class GaiaEdgeCertificateRuntimeConfiguration
{
	public bool IsEnabled { get; set; }

	public bool UseWildcard { get; set; } = true;

	public string AccountEmail { get; set; }

	public string DnsZoneNameOverride { get; set; }

	public string OrganizationUnit { get; set; } = "PlatformOps";

	public Uri AcmeServerUri { get; set; }
}

internal static class GaiaEdgeCertificateRuntimeResolver
{
	public const string EnabledEnvironmentVariableName = "MANOIR_EDGE_CERTIFICATE_ENABLED";

	public const string AccountEmailEnvironmentVariableName = "MANOIR_EDGE_CERTIFICATE_ACCOUNT_EMAIL";

	public const string UseWildcardEnvironmentVariableName = "MANOIR_EDGE_CERTIFICATE_USE_WILDCARD";

	public const string DnsZoneNameEnvironmentVariableName = "MANOIR_EDGE_CERTIFICATE_DNS_ZONE_NAME";

	public const string OrganizationUnitEnvironmentVariableName = "MANOIR_EDGE_CERTIFICATE_ORGANIZATION_UNIT";

	public const string AcmeServerUriEnvironmentVariableName = "MANOIR_EDGE_CERTIFICATE_ACME_SERVER_URI";

	public static bool ShouldReactToMeshPublicBaseDomainChanges()
	{
		bool? configuredEnabled = ReadBooleanEnvironmentVariable(EnabledEnvironmentVariableName);
		if (configuredEnabled.HasValue)
			return configuredEnabled.Value;

		return true;
	}

	public static async Task<GaiaEdgeCertificateRuntimeConfiguration> ResolveAsync(CancellationToken cancellationToken = default)
	{
		string accountEmail = Environment.GetEnvironmentVariable(AccountEmailEnvironmentVariableName)?.Trim();
		if (string.IsNullOrWhiteSpace(accountEmail))
			accountEmail = await ResolveAdminUserEmailAsync(cancellationToken);

		bool? configuredEnabled = ReadBooleanEnvironmentVariable(EnabledEnvironmentVariableName);
		bool isEnabled = configuredEnabled ?? !string.IsNullOrWhiteSpace(accountEmail);

		return new GaiaEdgeCertificateRuntimeConfiguration()
		{
			IsEnabled = isEnabled,
			UseWildcard = ReadBooleanEnvironmentVariable(UseWildcardEnvironmentVariableName) ?? true,
			AccountEmail = accountEmail,
			DnsZoneNameOverride = Environment.GetEnvironmentVariable(DnsZoneNameEnvironmentVariableName)?.Trim(),
			OrganizationUnit = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(OrganizationUnitEnvironmentVariableName))
				? "PlatformOps"
				: Environment.GetEnvironmentVariable(OrganizationUnitEnvironmentVariableName).Trim(),
			AcmeServerUri = ResolveOptionalAbsoluteUri(Environment.GetEnvironmentVariable(AcmeServerUriEnvironmentVariableName))
		};
	}

	private static async Task<string> ResolveAdminUserEmailAsync(CancellationToken cancellationToken)
	{
		var adminUser = await new UserLogic().GetAdminUserAsync(cancellationToken);
		return string.IsNullOrWhiteSpace(adminUser?.MainEmail)
			? null
			: adminUser.MainEmail.Trim();
	}

	private static bool? ReadBooleanEnvironmentVariable(string environmentVariableName)
	{
		string rawValue = Environment.GetEnvironmentVariable(environmentVariableName);
		if (string.IsNullOrWhiteSpace(rawValue))
			return null;

		if (bool.TryParse(rawValue.Trim(), out bool parsedBoolean))
			return parsedBoolean;

		return rawValue.Trim() switch
		{
			"1" => true,
			"0" => false,
			_ => null
		};
	}

	private static Uri ResolveOptionalAbsoluteUri(string rawValue)
	{
		if (string.IsNullOrWhiteSpace(rawValue))
			return null;

		return new Uri(rawValue.Trim(), UriKind.Absolute);
	}
}