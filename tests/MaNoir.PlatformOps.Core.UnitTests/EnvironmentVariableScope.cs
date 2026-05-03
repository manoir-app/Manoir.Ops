using System;

namespace MaNoir.PlatformOps.Core.UnitTests;

public sealed class EnvironmentVariableScope : IDisposable
{
	private readonly string _name;
	private readonly string _previousValue;

	public EnvironmentVariableScope(string name, string value)
	{
		_name = name;
		_previousValue = Environment.GetEnvironmentVariable(name);
		Environment.SetEnvironmentVariable(name, value);
	}

	public void Dispose()
	{
		Environment.SetEnvironmentVariable(_name, _previousValue);
	}
}