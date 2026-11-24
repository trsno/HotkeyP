using HotkeyP.Commands;

namespace HotkeyP.Core.Tests;

public class CommandCoverageTests
{
    [Fact]
    public void EveryCatalogCommandIsImplemented()
    {
        var t = new CommandTable();
        CommandRunner.RegisterAll(t);
        var missing = CommandCatalog.All.Where(c => !t.Contains(c.Id)).Select(c => $"{c.Id} {c.Name}").ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void CatalogIdsMatchPositions()
    {
        for (int i = 0; i < CommandCatalog.All.Count; i++) Assert.Equal(i, CommandCatalog.All[i].Id);
    }
}
