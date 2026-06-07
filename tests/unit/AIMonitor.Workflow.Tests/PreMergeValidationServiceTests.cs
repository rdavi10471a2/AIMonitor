using AIMonitor.Core;
using AIMonitor.Workflow;

namespace AIMonitor.Workflow.Tests;

public sealed class PreMergeValidationServiceTests
{
    [Fact]
    public void Validate_uses_explicit_project_plan_instead_of_full_solution_build()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "AIMonitorWorkflowTests", Guid.NewGuid().ToString("N"));
        string repositoryRoot = Path.Combine(tempRoot, "Repo");
        string runtimeRoot = Path.Combine(tempRoot, "Runtime");
        string watchedRoot = Path.Combine(tempRoot, "Watched");
        string appRoot = Path.Combine(watchedRoot, "App");
        string brokenRoot = Path.Combine(watchedRoot, "Broken");
        Directory.CreateDirectory(appRoot);
        Directory.CreateDirectory(brokenRoot);

        string solutionPath = Path.Combine(watchedRoot, "Fixture.slnx");
        string appProjectPath = Path.Combine(appRoot, "App.csproj");
        string brokenProjectPath = Path.Combine(brokenRoot, "Broken.csproj");
        string appSourcePath = Path.Combine(appRoot, "Program.cs");
        string stagedPath = Path.Combine(tempRoot, "staged.cs");

        File.WriteAllText(solutionPath, """
            <Solution>
              <Project Path="App\App.csproj" />
              <Project Path="Broken\Broken.csproj" />
            </Solution>
            """);
        File.WriteAllText(appProjectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(brokenProjectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(appSourcePath, """
            namespace App;

            public sealed class Program
            {
            }
            """);
        File.WriteAllText(Path.Combine(brokenRoot, "Broken.cs"), "namespace Broken { public sealed class Broken {");
        File.WriteAllText(stagedPath, """
            namespace App;

            public sealed class Program
            {
                public static string Marker => nameof(Marker);
            }
            """);

        MonitorSettings settings = MonitorSettings.Create(repositoryRoot, solutionPath, runtimeRoot);
        StagedEditRecord record = new()
        {
            StagedRecordId = "staged-test",
            WatchedFilePath = appSourcePath,
            StagedFilePath = stagedPath,
            RelativePath = Path.GetRelativePath(watchedRoot, appSourcePath),
            StagedHash = FileHash.Compute(stagedPath)
        };

        PreMergeValidationResult result = new PreMergeValidationService().Validate(
            settings,
            record,
            new PreMergeValidationPlan
            {
                OwningProjectPath = appProjectPath
            });

        Assert.False(result.IsError, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal("project", result.ValidationMode);
        Assert.EndsWith(Path.Combine("App", "App.csproj"), result.ValidationTargetPath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("project build passed", result.Message, StringComparison.OrdinalIgnoreCase);
    }
}
