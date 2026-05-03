using Microsoft.VisualStudio.TestTools.UnitTesting;
using MaNoir.PlatformOps.Core;

namespace MaNoir.PlatformOps.Core.UnitTests;

[TestClass]
public sealed class PlatformOpsNamingTests
{
	[TestMethod]
	public void NormalizeSegment_ShouldLowercaseAndCollapseSeparators()
	{
		string normalizedValue = PlatformOpsNaming.NormalizeSegment(" Prod__West 01 ");

		Assert.AreEqual("prod-west-01", normalizedValue);
	}

	[TestMethod]
	public void NormalizeSegment_ShouldUseFallbackWhenValueIsMissing()
	{
		string normalizedValue = PlatformOpsNaming.NormalizeSegment(null, "Default Scope");

		Assert.AreEqual("default-scope", normalizedValue);
	}
}
