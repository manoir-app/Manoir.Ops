using System;

namespace MaNoir.PlatformOps.Core;

public sealed class PlatformOpsSecretsRuntimeConfiguration
{
	public string ApiKey { get; set; }

	public string SaltBase64 { get; set; }

	public string AuthJwtSigningKey { get; set; }

	public byte[] SaltBytes { get; set; } = Array.Empty<byte>();
}