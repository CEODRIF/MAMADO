namespace AusbildungsWegInterview.App;

/// <summary>
/// Lifecycle of an imported personal document (CV / job description).
///
///   NotImported ──import/type──▶ Review ──use──▶ InUse
///        ▲                           │  ▲           │
///        └────────── clear ◀─────────┘  └── edit ───┘
///
/// Two texts are kept separate on purpose:
///  * <see cref="Text"/> — what the review box shows (imported or typed);
///  * <see cref="ActiveText"/> — what actually grounds answer generation.
///
/// Key rule: imported/typed text is NEVER grounded until the user has reviewed
/// it and explicitly pressed "Use". While InUse, a NEW import goes to the box
/// as Review while the old grounding text stays active until the new one is
/// accepted (replace = import → review → use). While InUse, edits to the box
/// re-apply live: whatever is visible in the box is what is grounded.
///
/// Content is memory-only: this type (and the app) never writes it to disk,
/// logs, telemetry, or anywhere else.
/// </summary>
public enum DocumentState
{
    NotImported,
    Review,
    InUse
}

public sealed class ReviewableDocument
{
    /// <summary>Text shown in the review box (imported or typed/pasted).</summary>
    public string Text { get; private set; } = string.Empty;

    /// <summary>The text currently grounding answers (non-null while InUse).</summary>
    public string? ActiveText { get; private set; }

    public DocumentState State { get; private set; } = DocumentState.NotImported;

    public bool IsInUse => State == DocumentState.InUse;
    public bool IsInReview => State == DocumentState.Review;

    /// <summary>Raised on every state/text change (UI re-render).</summary>
    public event Action? Changed;

    /// <summary>
    /// Puts extracted file text (or replacement text) into the review box.
    /// Does NOT touch the active grounding text.
    /// </summary>
    public void SetImported(string text)
    {
        Text = text ?? string.Empty;
        State = DocumentState.Review;
        Changed?.Invoke();
    }

    /// <summary>
    /// Accepts the currently visible text as grounding material (replace).
    /// Returns false + the localized-error key when the text is empty.
    /// </summary>
    public bool TryApply(out string errorKey)
    {
        if (string.IsNullOrWhiteSpace(Text))
        {
            errorKey = "ErrorDocEmptyUse";
            return false;
        }
        ActiveText = Text;
        errorKey = string.Empty;
        State = DocumentState.InUse;
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Reflects an edit in the review box (two-way binding source update).
    /// InUse: the grounding text follows the box (live re-apply).
    /// NotImported: typed/pasted text becomes a pending review.
    /// </summary>
    public void Edit(string newText)
    {
        var value = newText ?? string.Empty;
        if (value == Text)
            return;
        Text = value;

        if (State == DocumentState.InUse)
        {
            ActiveText = value; // visible text IS the grounded text
        }
        else if (State == DocumentState.NotImported && value.Length > 0)
        {
            State = DocumentState.Review;
        }

        Changed?.Invoke();
    }

    /// <summary>Removes the box text AND stops grounding with it.</summary>
    public void Clear()
    {
        if (Text.Length == 0 && State == DocumentState.NotImported)
            return;
        Text = string.Empty;
        ActiveText = null;
        State = DocumentState.NotImported;
        Changed?.Invoke();
    }
}
