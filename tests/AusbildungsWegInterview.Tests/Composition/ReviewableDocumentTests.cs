using AusbildungsWegInterview.App;
using Xunit;

namespace AusbildungsWegInterview.Tests.Composition;

/// <summary>
/// Review-before-use state machine for imported personal documents:
/// import lands in REVIEW (never auto-grounded), Use grounds it, edits while
/// in use re-apply live, a replacement import does NOT dislodge the active
/// grounding text until it is accepted, and Clear removes text AND grounding.
/// </summary>
public class ReviewableDocumentTests
{
    [Fact]
    public void Initial_IsNotImported_WithNothingGrounded()
    {
        var d = new ReviewableDocument();
        Assert.Equal(DocumentState.NotImported, d.State);
        Assert.Empty(d.Text);
        Assert.Null(d.ActiveText);
        Assert.False(d.IsInUse);
    }

    [Fact]
    public void Import_GoesToReview_NeverGroundsDirectly()
    {
        var d = new ReviewableDocument();
        d.SetImported("imported text");
        Assert.Equal(DocumentState.Review, d.State);
        Assert.Equal("imported text", d.Text);
        Assert.Null(d.ActiveText); // NOT available to answer generation yet
        Assert.False(d.IsInUse);
    }

    [Fact]
    public void UseOnEmptyText_IsRejected_WithErrorKey()
    {
        var d = new ReviewableDocument();
        Assert.False(d.TryApply(out var err));
        Assert.Equal("ErrorDocEmptyUse", err);

        d.SetImported("   "); // whitespace-only import
        Assert.False(d.TryApply(out err));
        Assert.Equal("ErrorDocEmptyUse", err);
        Assert.False(d.IsInUse);
    }

    [Fact]
    public void Use_GroundsTheReviewedText()
    {
        var d = new ReviewableDocument();
        d.SetImported("reviewed text");
        Assert.True(d.TryApply(out _));
        Assert.Equal(DocumentState.InUse, d.State);
        Assert.Equal("reviewed text", d.ActiveText);
        Assert.True(d.IsInUse);
    }

    [Fact]
    public void EditWhileInUse_ReappliesLive()
    {
        var d = new ReviewableDocument();
        d.SetImported("v1");
        d.TryApply(out _);
        d.Edit("v2 edited");
        Assert.Equal(DocumentState.InUse, d.State);
        Assert.Equal("v2 edited", d.ActiveText); // grounding follows the visible box
        Assert.Equal("v2 edited", d.Text);
    }

    [Fact]
    public void TypingWhileNotImported_BecomesPendingReview()
    {
        var d = new ReviewableDocument();
        d.Edit("pasted directly");
        Assert.Equal(DocumentState.Review, d.State);
        Assert.Null(d.ActiveText); // still not grounded until Use
    }

    [Fact]
    public void ReplacementImport_KeepsOldTextActive_UntilAccepted()
    {
        var d = new ReviewableDocument();
        d.SetImported("old active text");
        d.TryApply(out _);

        // Replace flow: import new content while the old one is active.
        d.SetImported("new pending text");
        Assert.Equal(DocumentState.Review, d.State);
        Assert.Equal("new pending text", d.Text);
        Assert.Equal("old active text", d.ActiveText); // old grounding stays active

        d.TryApply(out _);
        Assert.Equal(DocumentState.InUse, d.State);
        Assert.Equal("new pending text", d.ActiveText); // now replaced
    }

    [Fact]
    public void Clear_RemovesTextAndGrounding()
    {
        var d = new ReviewableDocument();
        d.SetImported("text");
        d.TryApply(out _);
        d.Clear();
        Assert.Equal(DocumentState.NotImported, d.State);
        Assert.Empty(d.Text);
        Assert.Null(d.ActiveText);
        Assert.False(d.IsInUse);
    }

    [Fact]
    public void Changes_RaiseEvents_OnEveryTransition()
    {
        var d = new ReviewableDocument();
        var events = 0;
        d.Changed += () => events++;
        d.SetImported("a");
        d.TryApply(out _);
        d.Edit("b");
        d.Clear();
        d.Clear(); // no-op clear: no extra event
        Assert.Equal(4, events);
    }
}
