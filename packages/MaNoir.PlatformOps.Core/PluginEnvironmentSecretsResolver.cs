using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MaNoir.Core.Secrets;

namespace MaNoir.PlatformOps.Core;

public static class PluginEnvironmentSecretsResolver
{
	public static Task<IReadOnlyList<PluginResolvedEnvironmentVariable>> ResolveAsync(IReadOnlyList<PluginEnvironmentVariable> variables, CancellationToken cancellationToken = default)
	{
		SharedSecretLogic sharedSecretLogic = new SharedSecretLogic();
		return ResolveAsync(variables, sharedSecretLogic.GetSecretAsync, cancellationToken);
	}

	public static async Task<IReadOnlyList<PluginResolvedEnvironmentVariable>> ResolveAsync(IReadOnlyList<PluginEnvironmentVariable> variables, Func<string, CancellationToken, Task<string>> resolveSecretAsync, CancellationToken cancellationToken = default)
	{
		if (variables == null)
			throw new ArgumentNullException(nameof(variables));

		if (resolveSecretAsync == null)
			throw new ArgumentNullException(nameof(resolveSecretAsync));

		List<string> errors = new List<string>();
		List<PluginResolvedEnvironmentVariable> resolvedVariables = new List<PluginResolvedEnvironmentVariable>();

		foreach (PluginEnvironmentVariable variable in variables)
		{
			if (variable == null)
				continue;

			if (variable.ValueKind == PluginEnvironmentValueKind.SecretReference)
			{
				string secretValue = await resolveSecretAsync(variable.SecretName, cancellationToken);
				if (secretValue == null)
				{
					errors.Add($"The shared secret '{variable.SecretName}' referenced by '{variable.Name}' was not found.");
					continue;
				}

				resolvedVariables.Add(new PluginResolvedEnvironmentVariable()
				{
					Name = variable.Name,
					Value = secretValue,
					ValueKind = variable.ValueKind,
					SecretName = variable.SecretName
				});
				continue;
			}

			resolvedVariables.Add(new PluginResolvedEnvironmentVariable()
			{
				Name = variable.Name,
				Value = variable.LiteralValue,
				ValueKind = variable.ValueKind,
				SecretName = variable.SecretName
			});
		}

		if (errors.Count > 0)
			throw new PluginEnvironmentSecretsResolutionException(errors);

		return resolvedVariables;
	}
}