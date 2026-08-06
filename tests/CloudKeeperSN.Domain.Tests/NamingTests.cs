using CloudKeeperSN.Domain.Naming;

namespace CloudKeeperSN.Domain.Tests;

public sealed class NamingTests
{
    [Theory]
    [InlineData("báo:cáo?.docx", "báo_cáo_.docx")]
    [InlineData("kế hoạch. ", "kế hoạch")]
    [InlineData("a/b\\c.txt", "a_b_c.txt")]
    public void Normalize_ReplacesIllegalOneDriveCharacters(string input, string expected)
    {
        Assert.Equal(expected, OneDriveNameNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("CON", "_CON")]
    [InlineData("aux.txt", "_aux.txt")]
    [InlineData("desktop.ini", "_desktop.ini")]
    public void Normalize_ProtectsReservedNames(string input, string expected)
    {
        Assert.Equal(expected, OneDriveNameNormalizer.Normalize(input));
    }

    [Fact]
    public void ConflictName_IsDeterministicAndKeepsExtension()
    {
        var policy = new DeterministicConflictNamePolicy();

        var first = policy.CreateSafeName("Báo cáo.final.docx", 2);
        var second = policy.CreateSafeName("Báo cáo.final.docx", 2);

        Assert.Equal("Báo cáo.final (CloudKeeperSN 2).docx", first);
        Assert.Equal(first, second);
    }
}

