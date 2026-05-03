using Microsoft.VisualStudio.TestTools.UnitTesting;
using MaNoir.PlatformOps.Core;

namespace MaNoir.PlatformOps.Core.UnitTests;

[TestClass]
public sealed class PluginManifestParserTests
{
	[TestMethod]
	public void Parse_ShouldReadManifestSections()
	{
		PluginManifest manifest = PluginManifestParser.Parse(@"
apiVersion: manoir/v1
kind: PluginManifest

plugin:
  pluginId: sarah
  repoUrl: https://github.com/manoir-app/manoir-plugin-sarah
  displayName: Sarah Home Agent
  publisher: MaNoir
  version: 2.3.1
  minimumMaNoirVersion: 1.8.0

documentation:
  overviewPath:
    fr-FR: docs/overview.fr.md
    en-US: docs/overview.md
  setupPath:
    fr-FR: docs/setup.fr.md
    en-US: docs/setup.md

dependencies:
  requiredPlugins:
    - pluginId: core-mqtt
      repoUrl: https://github.com/manoir-app/manoir-plugin-core-mqtt
      minVersion: 1.2.0
      optional: false
      reason:
        fr-FR: Necessaire pour la publication MQTT.
        en-US: Required for MQTT publication.

catalog:
  accessZones:
    - id: sarah.admin-ui
      label:
        fr-FR: Sarah Admin UI
        en-US: Sarah Admin UI
      description:
        fr-FR: Acces aux pages d'administration Sarah.
        en-US: Access to Sarah administration pages.
  contributions:
    - id: sarah.admin.pages
      kind: AdminUiPage
      label:
        fr-FR: Sarah Admin Pages
        en-US: Sarah Admin Pages
      description:
        fr-FR: Ajoute les pages d'administration Sarah.
        en-US: Adds Sarah administration pages.
      canCreateInstances: false
      adminUi:
        domain: Sarah
        accessZoneId: sarah.admin-ui
        requiredAccessLevel: Read
        pages:
          - category: Home
            name: Dashboard
            url: /admin/sarah
            labels:
              fr-FR: Tableau de bord
              en-US: Dashboard
    - id: sarah.mqtt.integration
      kind: Integration
      label:
        fr-FR: Sarah MQTT
        en-US: Sarah MQTT
      description:
        fr-FR: Publie et consomme des evenements domotiques MQTT.
        en-US: Publishes and consumes MQTT home automation events.
      canCreateInstances: true
      canInstallMultipleTimes: false
      tags:
        - mqtt
        - home-automation
      integration:
        domain: DailyLife
        category: HouseKeepingService
        serviceDependencyKind: Cloud
        requiresExternalSubscription: false
        documentationUrl: https://github.com/manoir-app/manoir-plugin-sarah/blob/main/docs/setup.md
        publishedEntityKinds:
          - kind: sensor:presence
            descriptions:
              fr-FR: Capteurs de presence
              en-US: Presence sensors

deployment:
  group: home-automation
  artifacts:
    - kind: compose
      path: deploy/docker-compose.yml
    - kind: env-template
      path: deploy/.env.template
");

		Assert.AreEqual("sarah", manifest.Plugin.PluginId);
		Assert.AreEqual("docs/overview.fr.md", manifest.Documentation.OverviewPath["fr-FR"]);
		Assert.AreEqual(1, manifest.Dependencies.RequiredPlugins.Count);
		Assert.AreEqual(2, manifest.Catalog.Contributions.Count);
		Assert.AreEqual("Read", manifest.Catalog.Contributions[0].AdminUi.RequiredAccessLevel);
    Assert.AreEqual("home-automation", manifest.Deployment.Group);
		Assert.AreEqual("compose", manifest.Deployment.Artifacts[0].Kind);
	}

  [TestMethod]
  public void Parse_ShouldRejectEmptyDeploymentGroup()
  {
    PluginManifestValidationException exception = Assert.ThrowsException<PluginManifestValidationException>(() => PluginManifestParser.Parse(@"
apiVersion: manoir/v1
kind: PluginManifest
plugin:
  pluginId: sarah
  repoUrl: https://github.com/manoir-app/manoir-plugin-sarah
  displayName: Sarah Home Agent
  publisher: MaNoir
  version: 2.3.1
  minimumMaNoirVersion: 1.8.0
deployment:
  group: '   '
"));

    CollectionAssert.Contains((System.Collections.ICollection)exception.Errors, "deployment.group must not be empty when provided.");
  }

	[TestMethod]
	public void Parse_ShouldRejectInvalidApiVersion()
	{
		PluginManifestValidationException exception = Assert.ThrowsException<PluginManifestValidationException>(() => PluginManifestParser.Parse(@"
apiVersion: manoir/v2
kind: PluginManifest
plugin:
  pluginId: sarah
  repoUrl: https://github.com/manoir-app/manoir-plugin-sarah
  displayName: Sarah Home Agent
  publisher: MaNoir
  version: 2.3.1
  minimumMaNoirVersion: 1.8.0
"));

		CollectionAssert.Contains((System.Collections.ICollection)exception.Errors, "apiVersion must be 'manoir/v1'.");
	}

	[TestMethod]
	public void Parse_ShouldRejectAbsoluteArtifactPath()
	{
		PluginManifestValidationException exception = Assert.ThrowsException<PluginManifestValidationException>(() => PluginManifestParser.Parse(@"
apiVersion: manoir/v1
kind: PluginManifest
plugin:
  pluginId: sarah
  repoUrl: https://github.com/manoir-app/manoir-plugin-sarah
  displayName: Sarah Home Agent
  publisher: MaNoir
  version: 2.3.1
  minimumMaNoirVersion: 1.8.0
deployment:
  artifacts:
    - kind: compose
      path: C:/deploy/docker-compose.yml
"));

		CollectionAssert.Contains((System.Collections.ICollection)exception.Errors, "deployment.artifacts[0].path must be a relative path.");
	}
}