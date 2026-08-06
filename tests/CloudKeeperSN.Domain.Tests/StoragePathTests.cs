using CloudKeeperSN.Domain.Storage;

namespace CloudKeeperSN.Domain.Tests;

public sealed class StoragePathTests
{
    [Fact]
    public void RelativeTo_PreservesAllNestedSegments()
    {
        var sourceRoot = new StoragePath(["Tài liệu"]);
        var item = new StoragePath(["Tài liệu", "Dự án", "2026", "kế hoạch.docx"]);

        var relative = item.RelativeTo(sourceRoot);

        Assert.Equal("Dự án/2026/kế hoạch.docx", relative.ToString());
    }

    [Fact]
    public void Constructor_RejectsTraversalSegments()
    {
        Assert.Throws<ArgumentException>(() => new StoragePath(["hợp lệ", "..", "bí mật"]));
    }
}

