using AIMonitor.MSBuild;

namespace AIMonitor.MSBuild.Tests;

public sealed class MSBuildWorkspaceLoaderTests
{
    [Fact]
    public async Task OpenProjectAsync_loads_sdk_style_project()
    {
        string root = Path.Combine(Path.GetTempPath(), "AIMonitorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string projectPath = Path.Combine(root, "Fixture.csproj");
        string sourcePath = Path.Combine(root, "Program.cs");

        await File.WriteAllTextAsync(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);

        await File.WriteAllTextAsync(sourcePath, """
            namespace Fixture;

            public sealed class Program
            {
                public static void Main()
                {
                }
            }
            """);

        MSBuildSolutionSnapshot snapshot = await new MSBuildWorkspaceLoader().OpenProjectAsync(projectPath);

        Assert.Single(snapshot.Projects);
        Assert.Equal("Fixture", snapshot.Projects[0].Name);
        Assert.Contains(snapshot.Projects[0].Documents, document => document.Name == "Program.cs");
    }
}
