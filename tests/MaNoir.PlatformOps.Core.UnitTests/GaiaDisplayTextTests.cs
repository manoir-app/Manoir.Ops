using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MaNoir.PlatformOps.Core;
using MaNoir.PlatformOps.Provider.Docker;

namespace MaNoir.PlatformOps.Core.UnitTests;

[TestClass]
public sealed class GaiaDisplayTextTests
{
	[TestMethod]
	public void GetLabel_ShouldReturnFrenchLabelForKnownServices()
	{
		Assert.AreEqual("Base de données", DockerServiceLabels.GetLabel("mongo"));
		Assert.AreEqual("Messagerie", DockerServiceLabels.GetLabel("nats"));
		Assert.AreEqual("Capteurs et appareils", DockerServiceLabels.GetLabel("MQTT"));
		Assert.AreEqual("Cache", DockerServiceLabels.GetLabel("redis"));
		Assert.AreEqual("Routeur Traefik", DockerServiceLabels.GetLabel("traefik"));
		Assert.AreEqual("Console d’administration", DockerServiceLabels.GetLabel("core"));
	}

	[TestMethod]
	public void GetLabel_ShouldReturnTechnicalNameForUnknownServices()
	{
		Assert.AreEqual("custom-service", DockerServiceLabels.GetLabel("custom-service"));
		Assert.AreEqual(string.Empty, DockerServiceLabels.GetLabel(null));
	}

	[TestMethod]
	public void ToFrench_ShouldTranslateKnownSecretErrors()
	{
		Assert.AreEqual(
			"La variable HOMEAUTOMATION_APIKEY est absente ou vide.",
			PlatformOpsEnvironmentErrorTranslator.ToFrench("HOMEAUTOMATION_APIKEY is required for PlatformOps secret operations."));
		Assert.AreEqual(
			"La variable HOMEAUTOMATION_SECRETS_SALT doit contenir une valeur Base64 valide.",
			PlatformOpsEnvironmentErrorTranslator.ToFrench("HOMEAUTOMATION_SECRETS_SALT must contain a valid Base64 payload."));
		Assert.AreEqual(
			"La variable HOMEAUTOMATION_SECRETS_SALT doit faire au moins 16 octets une fois décodée.",
			PlatformOpsEnvironmentErrorTranslator.ToFrench("HOMEAUTOMATION_SECRETS_SALT must decode to at least 16 bytes."));
		Assert.AreEqual(
			"La variable HOMEAUTOMATION_AUTH_JWT_SIGNING_KEY doit contenir au moins 32 caractères.",
			PlatformOpsEnvironmentErrorTranslator.ToFrench("HOMEAUTOMATION_AUTH_JWT_SIGNING_KEY must contain at least 32 characters."));
	}

	[TestMethod]
	public void ToFrench_ShouldKeepUnknownMessagesUnchanged()
	{
		IReadOnlyList<string> result = PlatformOpsEnvironmentErrorTranslator.ToFrench(new[] { "Something else happened." });

		Assert.AreEqual(1, result.Count);
		Assert.AreEqual("Something else happened.", result[0]);
	}

	[TestMethod]
	public void FirstRunStatus_ShouldExposeFrenchDisplayTexts()
	{
		DockerFirstRunStatus status = new DockerFirstRunStatus
		{
			EnvironmentErrors = new[] { "HOMEAUTOMATION_APIKEY is required for PlatformOps secret operations." }
		};
		DockerSharedServiceStatus service = new DockerSharedServiceStatus { ServiceName = "nats" };

		Assert.AreEqual("La variable HOMEAUTOMATION_APIKEY est absente ou vide.", status.EnvironmentIssues[0]);
		Assert.AreEqual("Messagerie", service.DisplayName);
	}
}
