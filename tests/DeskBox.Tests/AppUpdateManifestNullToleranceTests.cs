using System.Text.Json;
using DeskBox.Models;

namespace DeskBox.Tests;

/// <summary>
/// R18-S8-06: the manifest's localized dictionaries are declared with
/// <c>= []</c>, but System.Text.Json assigns an explicit JSON <c>null</c>
/// straight through the setter — the initializer only covers an absent
/// property. A remote manifest can therefore hand the display surfaces a null
/// dictionary, and <see cref="AppUpdateManifest.HasReleaseNotes"/> would throw
/// inside the update dialog. These tests pin the normalization.
/// </summary>
public sealed class AppUpdateManifestNullToleranceTests
{
    private const string ManifestWithExplicitNulls = """
        {
          "SchemaVersion": 1,
          "Version": "1.5.9",
          "DownloadUrl": "https://deskbox.fun/update/DeskBox-1.5.9-x64.exe",
          "Summary": null,
          "ReleaseNotes": null
        }
        """;

    [Fact]
    public void Deserialize_NormalizesExplicitNullCollections()
    {
        AppUpdateManifest? manifest =
            JsonSerializer.Deserialize<AppUpdateManifest>(ManifestWithExplicitNulls);

        Assert.NotNull(manifest);
        Assert.Empty(manifest!.Summary);
        Assert.Empty(manifest.ReleaseNotes);
    }

    [Fact]
    public void HasReleaseNotes_IsSafeForExplicitNullCollections()
    {
        AppUpdateManifest? manifest =
            JsonSerializer.Deserialize<AppUpdateManifest>(ManifestWithExplicitNulls);

        Assert.NotNull(manifest);
        Assert.False(manifest!.HasReleaseNotes);
        Assert.False(manifest.HasReleaseNotesOrUrl);
    }
}
