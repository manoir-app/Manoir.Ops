using Microsoft.VisualStudio.TestTools.UnitTesting;
using MaNoir.PlatformOps.Core;

namespace MaNoir.PlatformOps.Core.UnitTests;

[TestClass]
public sealed class AdminUiDeploymentProjectionFactoryTests
{
	[TestMethod]
	public void Create_ShouldComposeEffectivePathsFromPublicBasePathAndRelativePaths()
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
catalog:
  contributions:
    - id: sarah.admin.pages
      kind: adminui.page
      label:
        fr-FR: Sarah Admin Pages
      adminUi:
        domain: Sarah
        accessZoneId: sarah.admin-ui
        requiredAccessLevel: Read
        pages:
          - category: Home
            name: Dashboard
            relativePath: /admin
            labels:
              fr-FR: Tableau de bord
deployment:
  adminUi:
    pathPrefix: /home-automation
    composeService: admin-ui
    port: 8080
");

		PluginDeploymentDescriptor descriptor = PluginDeploymentDescriptorFactory.Create(manifest);
		AdminUiDeploymentProjection projection = AdminUiDeploymentProjectionFactory.Create(manifest, descriptor);

		Assert.AreEqual("sarah", projection.PluginId);
		Assert.AreEqual("/home-automation", projection.PublicBasePath);
		Assert.AreEqual(1, projection.Contributions.Count);
		Assert.AreEqual("/admin", projection.Contributions[0].Pages[0].RelativePath);
		Assert.AreEqual("/home-automation/admin", projection.Contributions[0].Pages[0].EffectivePath);
	}

	[TestMethod]
	public void Create_ShouldFallbackToLegacyUrlWhenRelativePathIsMissing()
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
catalog:
  contributions:
    - id: sarah.admin.pages
      kind: adminui.page
      label:
        fr-FR: Sarah Admin Pages
      adminUi:
        domain: Sarah
        accessZoneId: sarah.admin-ui
        requiredAccessLevel: Read
        pages:
          - category: Home
            name: Dashboard
            url: https://demo.manoir.app/admin
            labels:
              fr-FR: Tableau de bord
deployment:
  adminUi:
    pathPrefix: /home-automation
    composeService: admin-ui
    port: 8080
");

		PluginDeploymentDescriptor descriptor = PluginDeploymentDescriptorFactory.Create(manifest);
		AdminUiDeploymentProjection projection = AdminUiDeploymentProjectionFactory.Create(manifest, descriptor);

		Assert.AreEqual("https://demo.manoir.app/admin", projection.Contributions[0].Pages[0].EffectivePath);
		Assert.AreEqual("https://demo.manoir.app/admin", projection.Contributions[0].Pages[0].LegacyUrl);
	}
}