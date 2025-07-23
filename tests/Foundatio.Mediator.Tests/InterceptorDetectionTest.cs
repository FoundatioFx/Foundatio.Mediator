using Foundatio.Xunit;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace Foundatio.Mediator.Tests;

public class InterceptorDetectionTest : TestWithLoggingBase
{
    public InterceptorDetectionTest(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public void ShouldDetectInterceptorsAreEnabled()
    {
        // This test verifies that interceptors are enabled in this test project
        // by checking if the interceptor source files are generated

        // If interceptors are enabled, the Generated folder should contain interceptor files
        string generatedPath = Path.Combine(Directory.GetCurrentDirectory(), "Generated");
        _logger.LogInformation("Looking for generated files in: {GeneratedPath}", generatedPath);

        if (Directory.Exists(generatedPath))
        {
            string[] interceptorFiles = Directory.GetFiles(generatedPath, "*Interceptors*.cs", SearchOption.AllDirectories);
            _logger.LogInformation("Found {Count} interceptor files: {Files}",
                interceptorFiles.Length,
                String.Join(", ", interceptorFiles.Select(Path.GetFileName)));

            // If interceptors are enabled, we should have interceptor files
            if (interceptorFiles.Length > 0)
            {
                _logger.LogInformation("✓ Interceptors are enabled - found interceptor files");
                Assert.True(true, "Interceptors are enabled");
                return;
            }
        }

        _logger.LogWarning("⚠ No interceptor files found - interceptors may not be enabled or no call sites detected");

        // Let's also check the project file properties
        string projectDir = GetProjectDirectory();
        string csprojPath = Path.Combine(projectDir, "Foundatio.Mediator.Tests.csproj");

        if (File.Exists(csprojPath))
        {
            string csprojContent = File.ReadAllText(csprojPath);
            bool hasInterceptorsNamespaces = csprojContent.Contains("InterceptorsNamespaces") ||
                                             csprojContent.Contains("InterceptorsPreviewNamespaces");

            _logger.LogInformation("Project file contains interceptor configuration: {HasConfig}", hasInterceptorsNamespaces);

            if (hasInterceptorsNamespaces)
            {
                _logger.LogInformation("✓ Interceptors are configured in project file");
                Assert.True(true, "Interceptors are configured");
            }
            else
            {
                _logger.LogWarning("⚠ No interceptor configuration found in project file");
                Assert.Fail("Interceptors are not configured");
            }
        }
        else
        {
            _logger.LogError("Could not find project file at: {CsprojPath}", csprojPath);
            Assert.Fail("Could not verify interceptor configuration");
        }
    }

    private static string GetProjectDirectory()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null && !directory.GetFiles("*.csproj").Any())
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? Directory.GetCurrentDirectory();
    }
}
