using System;
using System.Collections.Generic;

namespace MaNoir.PlatformOps.Core;

public sealed class PlatformOpsSharedSecretsResolutionException : Exception
{
	public PlatformOpsSharedSecretsResolutionException(IReadOnlyList<string> errors)
		: base("The PlatformOps shared secrets could not be resolved.")
	{
		Errors = errors ?? Array.Empty<string>();
	}

	public IReadOnlyList<string> Errors { get; }
}