using DaisyOS.Shell.Apps.Notes.Documents;
using Xunit;

namespace DaisyOS.Shell.Tests;

public sealed class NotesDocumentUxTests
{
    [Theory]
    [InlineData("")]
    [InlineData("\nText")]
    [InlineData("Text\n")]
    [InlineData("\nText\n\n")]
    [InlineData("\n\n")]
    public void PlainTextConversionPreservesEmptyParagraphs(string text)
    {
        Assert.Equal(text, NoteDocument.FromPlainText(text).ToPlainText());
    }
}
