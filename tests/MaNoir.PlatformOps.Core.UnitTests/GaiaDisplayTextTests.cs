using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MaNoir.PlatformOps.Core;
using MaNoir.PlatformOps.Provider.Docker;

namespace MaNoir.PlatformOps.Core.UnitTests;

[TestClass]
public sealed class GaiaDisplayTextTests
{
	[TestMethod]
	public void GetLabel_ShouldReturnEnglishLabelForKnownServices()
	{
		Assert.AreEqual("Database", DockerServiceLabels.GetLabel("mongo"));
		Assert.AreEqual("Messaging", DockerServiceLabels.GetLabel("nats"));
		Assert.AreEqual("Sensors and devices", DockerServiceLabels.GetLabel("MQTT"));
		Assert.AreEqual("Cache", DockerServiceLabels.GetLabel("redis"));
		Assert.AreEqual("Traefik router", DockerServiceLabels.GetLabel("traefik"));
		Assert.AreEqual("Administration console", DockerServiceLabels.GetLabel("core"));
	}

	[TestMethod]
	public void GetLabel_ShouldReturnTechnicalNameForUnknownServices()
	{
		Assert.AreEqual("custom-service", DockerServiceLabels.GetLabel("custom-service"));
		Assert.AreEqual(string.Empty, DockerServiceLabels.GetLabel(null));
	}

	[TestMethod]
	public void ToReadable_ShouldTranslateKnownSecretErrors()
	{
		Assert.AreEqual(
			"The variable HOMEAUTOMATION_APIKEY is missing or empty.",
			PlatformOpsEnvironmentErrorTranslator.ToReadable("HOMEAUTOMATION_APIKEY is required for PlatformOps secret operations."));
		Assert.AreEqual(
			"The variable HOMEAUTOMATION_SECRETS_SALT must contain a valid Base64 value.",
			PlatformOpsEnvironmentErrorTranslator.ToReadable("HOMEAUTOMATION_SECRETS_SALT must contain a valid Base64 payload."));
		Assert.AreEqual(
			"The variable HOMEAUTOMATION_SECRETS_SALT must be at least 16 bytes once decoded.",
			PlatformOpsEnvironmentErrorTranslator.ToReadable("HOMEAUTOMATION_SECRETS_SALT must decode to at least 16 bytes."));
		Assert.AreEqual(
			"The variable HOMEAUTOMATION_AUTH_JWT_SIGNING_KEY must contain at least 32 characters.",
			PlatformOpsEnvironmentErrorTranslator.ToReadable("HOMEAUTOMATION_AUTH_JWT_SIGNING_KEY must contain at least 32 characters."));
	}

	[TestMethod]
	public void ToReadable_ShouldKeepUnknownMessagesUnchanged()
	{
		IReadOnlyList<string> result = PlatformOpsEnvironmentErrorTranslator.ToReadable(new[] { "Something else happened." });

		Assert.AreEqual(1, result.Count);
		Assert.AreEqual("Something else happened.", result[0]);
	}

	[TestMethod]
	public void FirstRunStatus_ShouldExposeReadableDisplayTexts()
	{
		DockerFirstRunStatus status = new DockerFirstRunStatus
		{
			EnvironmentErrors = new[] { "HOMEAUTOMATION_APIKEY is required for PlatformOps secret operations." }
		};
		DockerSharedServiceStatus service = new DockerSharedServiceStatus { ServiceName = "nats" };

		Assert.AreEqual("The variable HOMEAUTOMATION_APIKEY is missing or empty.", status.EnvironmentIssues[0]);
		Assert.AreEqual("Messaging", service.DisplayName);
	}
}
