using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MaNoir.PlatformOps.Core;

namespace MaNoir.PlatformOps.Core.UnitTests;

[TestClass]
public sealed class AcmeDnsCertificateInfrastructureTests
{
	[TestMethod]
	public void NormalizeOrderedDomainName_ShouldPreserveLiteralHost()
	{
		string orderedDomainName = AcmeDnsCertificateIssuer.NormalizeOrderedDomainName("public.manoir.app", false);

		Assert.AreEqual("public.manoir.app", orderedDomainName);
	}

	[TestMethod]
	public void NormalizeOrderedDomainName_ShouldPrefixWildcardWhenRequested()
	{
		string orderedDomainName = AcmeDnsCertificateIssuer.NormalizeOrderedDomainName("manoir.app", true);

		Assert.AreEqual("*.manoir.app", orderedDomainName);
	}

	[TestMethod]
	public void BuildDnsChallengeRecordName_ShouldStripWildcardPrefix()
	{
		string recordName = AcmeDnsCertificateIssuer.BuildDnsChallengeRecordName("*.manoir.app");

		Assert.AreEqual("_acme-challenge.manoir.app", recordName);
	}

	[TestMethod]
	public void GetRelativeRecordName_ShouldResolveRecordInsideZone()
	{
		string relativeRecordName = OvhDnsTxtChallengeWriter.GetRelativeRecordName("_acme-challenge.public.manoir.app", "manoir.app");

		Assert.AreEqual("_acme-challenge.public", relativeRecordName);
	}

	[TestMethod]
	public async Task ResolveAsync_ShouldResolveOvhCredentialsFromSharedSecrets()
	{
		OvhDnsChallengeCredentials credentials = await OvhDnsChallengeCredentialsResolver.ResolveAsync(
			new OvhDnsChallengeSecretConfiguration()
			{
				ZoneName = "manoir.app",
				ApplicationKeySecretName = PlatformOpsSharedSecretNames.OvhDnsApplicationKey,
				ApplicationSecretSecretName = PlatformOpsSharedSecretNames.OvhDnsApplicationSecret,
				ConsumerKeySecretName = PlatformOpsSharedSecretNames.OvhDnsConsumerKey
			},
			(secretName, cancellationToken) => Task.FromResult(secretName switch
			{
				PlatformOpsSharedSecretNames.OvhDnsApplicationKey => "app-key",
				PlatformOpsSharedSecretNames.OvhDnsApplicationSecret => "app-secret",
				PlatformOpsSharedSecretNames.OvhDnsConsumerKey => "consumer-key",
				_ => null
			}),
			CancellationToken.None);

		Assert.AreEqual("manoir.app", credentials.ZoneName);
		Assert.AreEqual("app-key", credentials.ApplicationKey);
		Assert.AreEqual("app-secret", credentials.ApplicationSecret);
		Assert.AreEqual("consumer-key", credentials.ConsumerKey);
		Assert.AreEqual("https://eu.api.ovh.com/1.0", credentials.ApiBaseUrl);
		Assert.AreEqual(360, credentials.RecordTtl);
	}

	[TestMethod]
	public async Task ResolveAsync_ShouldRejectMissingOvhSharedSecret()
	{
		PlatformOpsSharedSecretsResolutionException exception = await Assert.ThrowsExceptionAsync<PlatformOpsSharedSecretsResolutionException>(() => OvhDnsChallengeCredentialsResolver.ResolveAsync(
			new OvhDnsChallengeSecretConfiguration()
			{
				ZoneName = "manoir.app",
				ApplicationKeySecretName = PlatformOpsSharedSecretNames.OvhDnsApplicationKey,
				ApplicationSecretSecretName = PlatformOpsSharedSecretNames.OvhDnsApplicationSecret,
				ConsumerKeySecretName = PlatformOpsSharedSecretNames.OvhDnsConsumerKey
			},
			(secretName, cancellationToken) => Task.FromResult(secretName switch
			{
				PlatformOpsSharedSecretNames.OvhDnsApplicationKey => "app-key",
				PlatformOpsSharedSecretNames.OvhDnsApplicationSecret => "app-secret",
				_ => null
			}),
			CancellationToken.None));

		CollectionAssert.Contains((System.Collections.ICollection)exception.Errors, "The shared secret 'PLATFORMOPS_OVH_DNS_CONSUMER_KEY' referenced by 'ConsumerKeySecretName' was not found.");
	}
}