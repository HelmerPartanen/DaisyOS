using DaisyOS.Shell.Apps.Notes;
using Xunit;

namespace DaisyOS.Shell.Tests;

public class NotesViewModelTests
{
    [Fact]
    public void NotesViewModel_CanBeInstantiated()
    {
        var vm = new NotesViewModel();
        Assert.NotNull(vm);
    }
}
