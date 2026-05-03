using System;
using System.Collections.Generic;

namespace MaNoir.PlatformOps.Core;

public sealed class PlatformOpsKernel
{
	private readonly Dictionary<DeploymentTargetKind, Func<DeploymentTarget, DeploymentTarget>> _normalizers;

	public PlatformOpsKernel()
	{
		_normalizers = new Dictionary<DeploymentTargetKind, Func<DeploymentTarget, DeploymentTarget>>();
	}

	public IReadOnlyCollection<DeploymentTargetKind> RegisteredKinds => _normalizers.Keys;

	public PlatformOpsKernel RegisterNormalizer(DeploymentTargetKind kind, Func<DeploymentTarget, DeploymentTarget> normalizeTarget)
	{
		if (normalizeTarget == null)
			throw new ArgumentNullException(nameof(normalizeTarget));

		if (kind == DeploymentTargetKind.Unknown)
			throw new ArgumentException("A concrete target kind is required.", nameof(kind));

		_normalizers[kind] = normalizeTarget;
		return this;
	}

	public DeploymentTarget NormalizeTarget(DeploymentTarget target)
	{
		if (target == null)
			throw new ArgumentNullException(nameof(target));

		if (!_normalizers.TryGetValue(target.Kind, out Func<DeploymentTarget, DeploymentTarget> normalizeTarget))
			throw new InvalidOperationException("No deployment target provider is registered for the requested kind.");

		return normalizeTarget(target);
	}
}
