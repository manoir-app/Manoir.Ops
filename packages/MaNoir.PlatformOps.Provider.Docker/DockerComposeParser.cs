using System;
using System.Collections.Generic;
using System.IO;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace MaNoir.PlatformOps.Provider.Docker;

public static class DockerComposeParser
{
	public static DockerComposeFile Parse(string yamlText)
	{
		if (string.IsNullOrWhiteSpace(yamlText))
			throw new ArgumentException("A Docker Compose YAML document is required.", nameof(yamlText));

		try
		{
			YamlStream yamlStream = new YamlStream();
			yamlStream.Load(new StringReader(yamlText));

			if (yamlStream.Documents.Count == 0 || yamlStream.Documents[0].RootNode is not YamlMappingNode rootMapping)
				throw new DockerComposeValidationException(new[] { "The Docker Compose document must contain a root mapping." });

			return ParseRootMapping(rootMapping);
		}
		catch (YamlException exception)
		{
			throw new DockerComposeValidationException(new[] { "The Docker Compose YAML could not be parsed." }, exception);
		}
	}

	public static DockerComposeFile ParseFile(string composeFilePath)
	{
		if (string.IsNullOrWhiteSpace(composeFilePath))
			throw new ArgumentException("A Docker Compose file path is required.", nameof(composeFilePath));

		return Parse(File.ReadAllText(composeFilePath));
	}

	private static DockerComposeFile ParseRootMapping(YamlMappingNode rootMapping)
	{
		List<string> errors = new List<string>();
		DockerComposeFile composeFile = new DockerComposeFile()
		{
			Name = ReadScalar(rootMapping, "name"),
			Version = ReadScalar(rootMapping, "version")
		};

		if (!rootMapping.Children.TryGetValue(new YamlScalarNode("services"), out YamlNode servicesNode))
		{
			errors.Add("services is required.");
			throw new DockerComposeValidationException(errors);
		}

		if (servicesNode is not YamlMappingNode servicesMapping)
		{
			errors.Add("services must be a mapping.");
			throw new DockerComposeValidationException(errors);
		}

		List<DockerComposeService> services = new List<DockerComposeService>();

		foreach (KeyValuePair<YamlNode, YamlNode> entry in servicesMapping.Children)
		{
			string serviceName = (entry.Key as YamlScalarNode)?.Value;

			if (string.IsNullOrWhiteSpace(serviceName))
			{
				errors.Add("A service name is empty.");
				continue;
			}

			if (entry.Value is not YamlMappingNode serviceMapping)
			{
				errors.Add($"services.{serviceName} must be a mapping.");
				continue;
			}

			services.Add(ParseService(serviceName, serviceMapping, errors));
		}

		if (services.Count == 0 && errors.Count == 0)
			errors.Add("services must contain at least one service.");

		if (errors.Count > 0)
			throw new DockerComposeValidationException(errors);

		composeFile.Services = services;
		return composeFile;
	}

	private static DockerComposeService ParseService(string serviceName, YamlMappingNode serviceMapping, List<string> errors)
	{
		DockerComposeService service = new DockerComposeService()
		{
			Name = serviceName,
			Image = ReadScalar(serviceMapping, "image"),
			BuildContext = ReadBuildContext(serviceMapping),
			ContainerName = ReadScalar(serviceMapping, "container_name"),
			RestartPolicy = ReadScalar(serviceMapping, "restart"),
			Ports = ReadScalarSequence(serviceMapping, "ports", errors, $"services.{serviceName}.ports"),
			Volumes = ReadScalarSequence(serviceMapping, "volumes", errors, $"services.{serviceName}.volumes"),
			DependsOn = ReadDependsOn(serviceMapping, errors, serviceName),
			Environment = ReadEnvironment(serviceMapping, errors, serviceName)
		};

		if (string.IsNullOrWhiteSpace(service.Image) && string.IsNullOrWhiteSpace(service.BuildContext))
			errors.Add($"services.{serviceName} must declare either image or build.");

		return service;
	}

	private static string ReadBuildContext(YamlMappingNode serviceMapping)
	{
		if (!serviceMapping.Children.TryGetValue(new YamlScalarNode("build"), out YamlNode buildNode))
			return null;

		if (buildNode is YamlScalarNode buildScalar)
			return buildScalar.Value;

		if (buildNode is YamlMappingNode buildMapping)
			return ReadScalar(buildMapping, "context");

		return null;
	}

	private static IReadOnlyList<DockerComposeEnvironmentEntry> ReadEnvironment(YamlMappingNode serviceMapping, List<string> errors, string serviceName)
	{
		if (!serviceMapping.Children.TryGetValue(new YamlScalarNode("environment"), out YamlNode environmentNode))
			return Array.Empty<DockerComposeEnvironmentEntry>();

		List<DockerComposeEnvironmentEntry> environment = new List<DockerComposeEnvironmentEntry>();

		if (environmentNode is YamlMappingNode environmentMapping)
		{
			foreach (KeyValuePair<YamlNode, YamlNode> entry in environmentMapping.Children)
			{
				string name = (entry.Key as YamlScalarNode)?.Value;

				if (string.IsNullOrWhiteSpace(name))
				{
					errors.Add($"services.{serviceName}.environment contains an empty variable name.");
					continue;
				}

				environment.Add(new DockerComposeEnvironmentEntry()
				{
					Name = name,
					Value = (entry.Value as YamlScalarNode)?.Value
				});
			}

			return environment;
		}

		if (environmentNode is not YamlSequenceNode environmentSequence)
		{
			errors.Add($"services.{serviceName}.environment must be a mapping or a sequence.");
			return Array.Empty<DockerComposeEnvironmentEntry>();
		}

		foreach (YamlNode item in environmentSequence.Children)
		{
			string entryValue = (item as YamlScalarNode)?.Value;

			if (string.IsNullOrWhiteSpace(entryValue))
			{
				errors.Add($"services.{serviceName}.environment contains an empty sequence item.");
				continue;
			}

			int separatorIndex = entryValue.IndexOf('=');

			if (separatorIndex < 0)
			{
				environment.Add(new DockerComposeEnvironmentEntry()
				{
					Name = entryValue,
					Value = null
				});
				continue;
			}

			environment.Add(new DockerComposeEnvironmentEntry()
			{
				Name = entryValue.Substring(0, separatorIndex),
				Value = entryValue.Substring(separatorIndex + 1)
			});
		}

		return environment;
	}

	private static IReadOnlyList<string> ReadDependsOn(YamlMappingNode serviceMapping, List<string> errors, string serviceName)
	{
		if (!serviceMapping.Children.TryGetValue(new YamlScalarNode("depends_on"), out YamlNode dependsOnNode))
			return Array.Empty<string>();

		if (dependsOnNode is YamlSequenceNode dependsOnSequence)
			return ReadScalarSequence(dependsOnSequence, errors, $"services.{serviceName}.depends_on");

		if (dependsOnNode is YamlMappingNode dependsOnMapping)
		{
			List<string> dependsOn = new List<string>();

			foreach (KeyValuePair<YamlNode, YamlNode> entry in dependsOnMapping.Children)
			{
				string dependencyName = (entry.Key as YamlScalarNode)?.Value;

				if (string.IsNullOrWhiteSpace(dependencyName))
				{
					errors.Add($"services.{serviceName}.depends_on contains an empty dependency name.");
					continue;
				}

				dependsOn.Add(dependencyName);
			}

			return dependsOn;
		}

		errors.Add($"services.{serviceName}.depends_on must be a mapping or a sequence.");
		return Array.Empty<string>();
	}

	private static IReadOnlyList<string> ReadScalarSequence(YamlMappingNode mappingNode, string key, List<string> errors, string fieldName)
	{
		if (!mappingNode.Children.TryGetValue(new YamlScalarNode(key), out YamlNode node))
			return Array.Empty<string>();

		if (node is not YamlSequenceNode sequenceNode)
		{
			errors.Add(fieldName + " must be a sequence.");
			return Array.Empty<string>();
		}

		return ReadScalarSequence(sequenceNode, errors, fieldName);
	}

	private static IReadOnlyList<string> ReadScalarSequence(YamlSequenceNode sequenceNode, List<string> errors, string fieldName)
	{
		List<string> values = new List<string>();

		for (int index = 0; index < sequenceNode.Children.Count; index++)
		{
			string value = (sequenceNode.Children[index] as YamlScalarNode)?.Value;

			if (string.IsNullOrWhiteSpace(value))
			{
				errors.Add($"{fieldName}[{index}] must be a scalar value.");
				continue;
			}

			values.Add(value);
		}

		return values;
	}

	private static string ReadScalar(YamlMappingNode mappingNode, string key)
	{
		if (!mappingNode.Children.TryGetValue(new YamlScalarNode(key), out YamlNode node))
			return null;

		return (node as YamlScalarNode)?.Value;
	}
}