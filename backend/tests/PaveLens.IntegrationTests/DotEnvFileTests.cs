using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using PaveLens.Api.Infrastructure;

namespace PaveLens.IntegrationTests;

public class DotEnvFileTests
{
    [Fact]
    public void ParsesAliasesConfigPathsQuotesAndComments()
    {
        var values = DotEnvFile.Parse(
        [
            "# PaveLens",
            "",
            "PAVELENS_AI_PROVIDER=Meta",
            "export META_API_KEY=\"abc 123\"",
            "ROBOFLOW_API_KEY='rf-key'",
            "Ai__Meta__Reasoning__TimeoutSeconds=90 # seconds",
            "META_SAM31_API_KEY=",
            "UNRELATED=ignored",
            "not a pair",
        ]);

        Assert.Equal("Meta", values["Ai:Provider"]);
        Assert.Equal("abc 123", values["Ai:Meta:Reasoning:ApiKey"]);
        Assert.Equal("rf-key", values["Ai:Meta:Segmentation:ApiKey"]);
        Assert.Equal("90", values["Ai:Meta:Reasoning:TimeoutSeconds"]);
        Assert.False(values.ContainsKey("Ai:Meta:Sam31:ApiKey"));
        Assert.Equal(4, values.Count);
    }

    [Fact]
    public void EnvironmentVariablesOverrideTheFileAndTestingSkipsIt()
    {
        var dir = Directory.CreateTempSubdirectory("pavelens-dotenv-");
        try
        {
            File.WriteAllLines(Path.Combine(dir.FullName, ".env"), ["META_REASONING_MODEL=from-file", "ROBOFLOW_API_KEY=file-key"]);
            Environment.SetEnvironmentVariable("Ai__Meta__Segmentation__ApiKey", "env-key");

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development, ContentRootPath = dir.FullName });
            Assert.NotNull(DotEnvFile.AddTo(builder.Configuration, builder.Environment, dir.FullName));
            Assert.Equal("from-file", builder.Configuration["Ai:Meta:Reasoning:Model"]);
            Assert.Equal("env-key", builder.Configuration["Ai:Meta:Segmentation:ApiKey"]);

            var testing = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing", ContentRootPath = dir.FullName });
            Assert.Null(DotEnvFile.AddTo(testing.Configuration, testing.Environment, dir.FullName));
        }
        finally
        {
            Environment.SetEnvironmentVariable("Ai__Meta__Segmentation__ApiKey", null);
            dir.Delete(recursive: true);
        }
    }
}
