using System.Text.Json;
using Server.Tests.Support;

namespace Server.Tests.Architecture;

[Trait("Category", "Architecture")]
[Trait("Kind", "Architecture")]
public sealed class CodeStyleTests
{
  [Fact]
  public void AllFileTypesShareTheEightyColumnTarget()
  {
    var root = RepositoryFiles.Root();
    using var config = JsonDocument.Parse(
      File.ReadAllText(Path.Combine(root, ".csharpierrc.json"))
    );
    Assert.Equal(80, config.RootElement.GetProperty("printWidth").GetInt32());
    Assert.Equal(2, config.RootElement.GetProperty("tabWidth").GetInt32());
    using var client = JsonDocument.Parse(
      File.ReadAllText(Path.Combine(root, "Client", ".prettierrc.json"))
    );
    Assert.Equal(80, client.RootElement.GetProperty("printWidth").GetInt32());
    Assert.Equal(2, client.RootElement.GetProperty("tabWidth").GetInt32());
    Assert.False(config.RootElement.GetProperty("useTabs").GetBoolean());
    var editor = File.ReadAllText(Path.Combine(root, ".editorconfig"));
    Assert.Contains("[*.cs]", editor);
    var globalStart = editor.IndexOf("[*]", StringComparison.Ordinal);
    Assert.True(globalStart >= 0);
    var nextSection = editor.IndexOf('[', globalStart + 3);
    var globalSettings = editor[
      globalStart..(nextSection < 0 ? editor.Length : nextSection)
    ];
    Assert.Contains("max_line_length = 80", globalSettings);
    Assert.Single(
      editor.Split('\n'),
      line => line.StartsWith("max_line_length", StringComparison.Ordinal)
    );
    Assert.Contains("indent_size = 2", editor);
    using var manifest = JsonDocument.Parse(
      File.ReadAllText(Path.Combine(root, ".config", "dotnet-tools.json"))
    );
    Assert.Equal(
      "0.30.6",
      manifest
        .RootElement.GetProperty("tools")
        .GetProperty("csharpier")
        .GetProperty("version")
        .GetString()
    );
  }
}
