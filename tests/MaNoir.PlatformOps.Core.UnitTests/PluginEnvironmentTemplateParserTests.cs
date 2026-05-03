using Microsoft.VisualStudio.TestTools.UnitTesting;
using MaNoir.PlatformOps.Core;

namespace MaNoir.PlatformOps.Core.UnitTests;

[TestClass]
public sealed class PluginEnvironmentTemplateParserTests
{
	[TestMethod]
	public void Parse_ShouldReadLiteralAndSecretValues()
	{
		PluginEnvironmentTemplate template = PluginEnvironmentTemplateParser.Parse(@"
# Sarah environment
SARAH_PLUGIN_ID=sarah
SARAH_API_KEY=${{ secrets.SARAH_API_KEY }}
SARAH_MQTT__HOST=mqtt-broker
");

		Assert.AreEqual(3, template.Variables.Count);
		Assert.AreEqual(PluginEnvironmentValueKind.Literal, template.Variables[0].ValueKind);
		Assert.AreEqual("sarah", template.Variables[0].LiteralValue);
		Assert.AreEqual(PluginEnvironmentValueKind.SecretReference, template.Variables[1].ValueKind);
		Assert.AreEqual("SARAH_API_KEY", template.Variables[1].SecretName);
	}

	[TestMethod]
	public void Parse_ShouldRejectInvalidLineFormat()
	{
		PluginEnvironmentTemplateValidationException exception = Assert.ThrowsException<PluginEnvironmentTemplateValidationException>(() => PluginEnvironmentTemplateParser.Parse(@"
SARAH_PLUGIN_ID=sarah
BROKEN_LINE
"));

		CollectionAssert.Contains((System.Collections.ICollection)exception.Errors, "Line 3 must be in NAME=VALUE format.");
	}

	[TestMethod]
	public void Parse_ShouldRejectDuplicateVariables()
	{
		PluginEnvironmentTemplateValidationException exception = Assert.ThrowsException<PluginEnvironmentTemplateValidationException>(() => PluginEnvironmentTemplateParser.Parse(@"
SARAH_PLUGIN_ID=sarah
SARAH_PLUGIN_ID=other
"));

		CollectionAssert.Contains((System.Collections.ICollection)exception.Errors, "Variable 'SARAH_PLUGIN_ID' is declared more than once.");
	}
}