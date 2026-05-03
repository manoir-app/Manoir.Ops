using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MaNoir.PlatformOps.Provider.Docker;

namespace MaNoir.PlatformOps.Core.UnitTests;

[TestClass]
public sealed class DockerComposeParserTests
{
	[TestMethod]
	public void Parse_ShouldReadServicesFromComposeYaml()
	{
		DockerComposeFile composeFile = DockerComposeParser.Parse(@"
name: sarah-stack
services:
  api:
    image: manoir/sarah-api:2.3.1
    container_name: sarah-api
    restart: unless-stopped
    ports:
      - 8080:8080
    volumes:
      - ./data:/app/data
    environment:
      SARAH_PLUGIN_ID: sarah
      SARAH_API_KEY: ${SARAH_API_KEY}
  worker:
    build:
      context: .
    depends_on:
      - api
    environment:
      - WORKER_MODE=sync
      - MQTT_HOST
");

		Assert.AreEqual("sarah-stack", composeFile.Name);
		Assert.AreEqual(2, composeFile.Services.Count);
		Assert.AreEqual("manoir/sarah-api:2.3.1", composeFile.Services[0].Image);
		Assert.AreEqual(".", composeFile.Services[1].BuildContext);
		Assert.AreEqual("api", composeFile.Services[1].DependsOn[0]);
		Assert.AreEqual("SARAH_API_KEY", composeFile.Services[0].Environment[1].Name);
		Assert.AreEqual("WORKER_MODE", composeFile.Services[1].Environment[0].Name);
		Assert.IsNull(composeFile.Services[1].Environment[1].Value);
	}

	[TestMethod]
	public void Parse_ShouldRejectServiceWithoutImageOrBuild()
	{
		DockerComposeValidationException exception = Assert.ThrowsException<DockerComposeValidationException>(() => DockerComposeParser.Parse(@"
services:
  api:
    environment:
      MODE: local
"));

		CollectionAssert.Contains((System.Collections.ICollection)exception.Errors, "services.api must declare either image or build.");
	}

	[TestMethod]
	public void Parse_ShouldRejectInvalidEnvironmentShape()
	{
		DockerComposeValidationException exception = Assert.ThrowsException<DockerComposeValidationException>(() => DockerComposeParser.Parse(@"
services:
  api:
    image: manoir/sarah-api:2.3.1
    environment: local
"));

		CollectionAssert.Contains((System.Collections.ICollection)exception.Errors, "services.api.environment must be a mapping or a sequence.");
	}
}