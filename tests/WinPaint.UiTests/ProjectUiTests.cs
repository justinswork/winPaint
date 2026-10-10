using WinPaint.Core.Imaging;
using WinPaint.Core.Imaging.Codecs;
using WinPaint.Core.Projects;

namespace WinPaint.UiTests;

/// <summary>UI tests for the project format: .wpp files, embedded projects, plain saves, changed-outside images.</summary>
public class ProjectUiTests
{
    private static string TempPath(string name, string ext)
    {
        var dir = Path.Combine(Path.GetTempPath(), "winPaintUiTests");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{name}-{Guid.NewGuid():N}{ext}");
    }

    private static void AddText(AppSession s, string text)
    {
        s.Run("palette 0");
        s.Invoke("ToolText");
        s.Run("click 150 150");
        s.TypeInEditor(text);
        s.Run("click 1000 600");
        Assert.Equal(1, s.StateInt("texts"));
    }

    /// <summary>A .wpp keeps layers and text; Save writes back to the .wpp.</summary>
    [Fact]
    public void Wpp_SaveAndReopen_KeepsLayersAndText()
    {
        var file = TempPath("proj", ".wpp");
        using (var s = AppSession.Launch())
        {
            AddText(s, "Project text");
            s.Invoke("LayersToggle");
            s.Invoke("LayerAdd");
            Assert.Equal(2, s.StateInt("layers"));
            TextUiTests.SaveAs(s, file);
            Assert.Equal("Saved as a winPaint project", s.State("notice"));
            Assert.Equal("0", s.State("dirty"));
        }

        using (var s = AppSession.Launch(file))
        {
            AppSession.WaitUntil(() => s.State("file") == file, TimeSpan.FromSeconds(20));
            Assert.Equal(1, s.StateInt("texts"));
            Assert.Equal(2, s.StateInt("layers"));
            Assert.Contains(Path.GetFileName(file), s.State("title"), StringComparison.Ordinal);
            s.Run("palette 3; drag 300 300 400 380");
            var seq = s.Seq;
            s.RunNoWait("key Ctrl+S");
            s.WaitIdle(seq + 1);
            AppSession.WaitUntil(() => s.State("dirty") == "0", TimeSpan.FromSeconds(20), "Ctrl+S did not save the project");
            Assert.Equal(file, s.State("file"));
            s.Snapshot("WPP_01_reopened_project");
        }

        Assert.Equal("PK", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(file), 0, 2));
    }

    /// <summary>Save as plain image leaves no project in the file.</summary>
    [Fact]
    public void SaveAsPlainImage_ReopensWithoutEditableText()
    {
        var file = TempPath("plain", ".png");
        using (var s = AppSession.Launch())
        {
            AddText(s, "Plain");
            s.ExpandMenu("FileMenu");
            s.Invoke("FileSaveAsPlain");
            var dlg = s.WaitForNativeDialog();
            AppSession.FileDialogAccept(dlg, file);
            AppSession.WaitUntil(() => s.State("file") == file, TimeSpan.FromSeconds(20), "file was not saved");
        }

        Assert.Null(ProjectEmbedding.Extract(File.ReadAllBytes(file)));
        using (var s = AppSession.Launch(file))
        {
            AppSession.WaitUntil(() => s.State("file") == file, TimeSpan.FromSeconds(20));
            Assert.Equal(0, s.StateInt("texts"));
        }
    }

    /// <summary>An image edited by another program opens plain and offers the winPaint version.</summary>
    [Fact]
    public void ChangedOutside_OpensPlain_AndRestoresOnRequest()
    {
        var file = TempPath("outside", ".png");
        using (var s = AppSession.Launch())
        {
            AddText(s, "Original");
            TextUiTests.SaveAs(s, file);
            Assert.StartsWith("Editable text is saved inside this image", s.State("notice"), StringComparison.Ordinal);
        }

        // "Another program" changes the pixels but keeps the private chunk.
        var bytes = File.ReadAllBytes(file);
        var container = ProjectEmbedding.Extract(bytes)!;
        var image = ImageCodec.Decode(new MemoryStream(bytes)).Pixels;
        image.Fill(new PixelRect(0, 0, 20, 20), ColorUtil.Black);
        File.WriteAllBytes(file, ProjectEmbedding.Embed(ImageCodec.EncodePng(image), ImageFormat.Png, container));

        using (var s = AppSession.Launch(file))
        {
            AppSession.WaitUntil(() => s.State("file") == file, TimeSpan.FromSeconds(20));
            Assert.Equal(0, s.StateInt("texts"));
            Assert.Contains("changed outside winPaint", s.State("notice"), StringComparison.Ordinal);
            s.Snapshot("WPP_02_changed_outside");
            s.Invoke("RestoreProjectButton");
            AppSession.WaitUntil(() => s.StateInt("texts") == 1, TimeSpan.FromSeconds(20), "the winPaint version was not restored");
            Assert.Equal(string.Empty, s.State("file") ?? string.Empty);
            Assert.Equal("1", s.State("dirty"));
            s.Snapshot("WPP_03_restored");
        }
    }
}
