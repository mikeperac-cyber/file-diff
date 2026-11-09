using FileDiff.Core.Commander;

namespace FileDiff.Core.Tests;

public sealed class CommanderServiceTests
{
    [Fact]
    public void ResolveSource_Refuses_Traversal()
    {
        using var ws = new TempWorkspace();
        var svc = new CommanderService(ws.WatchRoot);
        Assert.Throws<InvalidOperationException>(() => svc.ResolveSource("../outside.txt"));
        Assert.Throws<InvalidOperationException>(() => svc.ResolveSource("a/../../b"));
    }

    [Fact]
    public void Copy_Move_Rename_Delete_Mkdir_Roundtrip()
    {
        using var ws = new TempWorkspace();
        ws.Write("a.txt", "hello");
        var svc = new CommanderService(ws.WatchRoot);

        var copy = svc.Copy("a.txt", "sub/b.txt");
        Assert.True(copy.Ok);
        Assert.True(File.Exists(ws.PathOf("sub/b.txt")));

        var move = svc.Move("sub/b.txt", "c.txt");
        Assert.True(move.Ok);
        Assert.True(File.Exists(ws.PathOf("c.txt")));
        Assert.False(File.Exists(ws.PathOf("sub/b.txt")));

        var rename = svc.Rename("c.txt", "d.txt");
        Assert.True(rename.Ok);
        Assert.True(File.Exists(ws.PathOf("d.txt")));

        var mkdir = svc.CreateDirectory("newdir/nested");
        Assert.True(mkdir.Ok);
        Assert.True(Directory.Exists(ws.PathOf("newdir/nested")));

        var del = svc.Delete("d.txt");
        Assert.True(del.Ok);
        Assert.False(File.Exists(ws.PathOf("d.txt")));

        var delDirBlocked = svc.Delete("newdir");
        Assert.False(delDirBlocked.Ok);

        var delDir = svc.Delete("newdir", recursive: true);
        Assert.True(delDir.Ok);
    }

    [Fact]
    public void QuickView_Text_And_Binary()
    {
        using var ws = new TempWorkspace();
        ws.Write("t.txt", "line1\nline2\nline3");
        ws.WriteBytes("b.bin", [0x41, 0x00, 0x42, 0x43]);
        var svc = new CommanderService(ws.WatchRoot);

        var text = svc.QuickView("t.txt");
        Assert.True(text.Exists);
        Assert.True(text.IsText);
        Assert.Contains("line1", text.PreviewText);

        var bin = svc.QuickView("b.bin");
        Assert.True(bin.Exists);
        Assert.False(bin.IsText);

        var missing = svc.QuickView("nope.txt");
        Assert.False(missing.Exists);
    }

    [Fact]
    public void Copy_To_Absolute_SecondPanel_Works()
    {
        using var ws = new TempWorkspace();
        ws.Write("a.txt", "x");
        var svc = new CommanderService(ws.WatchRoot);
        var outside = Path.Combine(ws.Root, "panel2", "a.txt");
        var r = svc.Copy("a.txt", outside);
        Assert.True(r.Ok);
        Assert.True(File.Exists(outside));
    }
}
