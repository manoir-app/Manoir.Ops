using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MaNoir.PlatformOps.Core;

namespace MaNoir.PlatformOps.Core.UnitTests;

[TestClass]
public sealed class PluginEnvironmentSecretsResolverTests
{
	[TestMethod]
	public async Task ResolveAsync_ShouldResolveLiteralAndSharedSecretValues()
	{
		IReadOnlyList<PluginResolvedEnvironmentVariable> resolvedVariables = await PluginEnvironmentSecretsResolver.ResolveAsync(
		[
			new PluginEnvironmentVariable() { Name = "PLUGIN_ID", ValueKind = PluginEnvironmentValueKind.Literal, LiteralValue = "sarah" },
			new PluginEnvironmentVariable() { Name = "API_KEY", ValueKind = PluginEnvironmentValueKind.SecretReference, SecretName = "SARAH_API_KEY" }
		],
			(secretName, cancellationToken) => Task.FromResult(secretName == "SARAH_API_KEY" ? "super-secret-value" : null),
			CancellationToken.None);

		Assert.AreEqual(2, resolvedVariables.Count);
		Assert.AreEqual("sarah", resolvedVariables[0].Value);
		Assert.AreEqual("super-secret-value", resolvedVariables[1].Value);
	}

	[TestMethod]
	public async Task ResolveAsync_ShouldRejectMissingSharedSecret()
	{
		PluginEnvironmentSecretsResolutionException exception = await Assert.ThrowsExceptionAsync<PluginEnvironmentSecretsResolutionException>(() => PluginEnvironmentSecretsResolver.ResolveAsync(
		[
			new PluginEnvironmentVariable() { Name = "API_KEY", ValueKind = PluginEnvironmentValueKind.SecretReference, SecretName = "SARAH_API_KEY" }
		],
			(secretName, cancellationToken) => Task.FromResult<string>(null),
			CancellationToken.None));

		CollectionAssert.Contains((System.Collections.ICollection)exception.Errors, "The shared secret 'SARAH_API_KEY' referenced by 'API_KEY' was not found.");
	}
}