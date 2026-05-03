using Microsoft.VisualStudio.TestTools.UnitTesting;
using MaNoir.PlatformOps.Core;

namespace MaNoir.PlatformOps.Core.UnitTests;

[TestClass]
public sealed class PluginDeploymentDescriptorFactoryTests
{
	[TestMethod]
	public void Create_ShouldProjectDeploymentDescriptor()
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
deployment:
  group: home-automation
  artifacts:
    - kind: compose
      path: deploy/docker-compose.yml
    - kind: env-template
      path: deploy/.env.template
");

		PluginEnvironmentTemplate environmentTemplate = PluginEnvironmentTemplateParser.Parse(@"
SARAH_PLUGIN_ID=sarah
SARAH_API_KEY=${{ secrets.SARAH_API_KEY }}
");

		PluginDeploymentDescriptor descriptor = PluginDeploymentDescriptorFactory.Create(manifest, environmentTemplate);

		Assert.AreEqual("sarah", descriptor.PluginId);
    Assert.AreEqual("home-automation", descriptor.DeploymentGroup);
		Assert.AreEqual("deploy/docker-compose.yml", descriptor.ComposeArtifactPath);
		Assert.AreEqual("deploy/.env.template", descriptor.EnvironmentTemplatePath);
		Assert.AreEqual(2, descriptor.EnvironmentVariables.Count);
		Assert.AreEqual(PluginEnvironmentValueKind.SecretReference, descriptor.EnvironmentVariables[1].ValueKind);
		Assert.AreEqual("SARAH_API_KEY", descriptor.EnvironmentVariables[1].SecretName);
	}

	[TestMethod]
	public void Create_ShouldRejectMultipleComposeArtifacts()
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
deployment:
  artifacts:
    - kind: compose
      path: deploy/docker-compose.yml
    - kind: compose
      path: deploy/docker-compose.override.yml
");

		PluginManifestValidationException exception = Assert.ThrowsException<PluginManifestValidationException>(() => PluginDeploymentDescriptorFactory.Create(manifest));

		CollectionAssert.Contains((System.Collections.ICollection)exception.Errors, "deployment.artifacts must not contain more than one compose artifact.");
	}
}