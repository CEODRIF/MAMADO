namespace AusbildungsWegInterview.Core.Domain;

/// <summary>
/// Optional, local-only candidate material used to ground answers. Kept entirely
/// on-device; never logged and never transmitted by default.
/// </summary>
public sealed record CandidateContext(string? CvText, string? JobDescription, string? CandidateName)
{
    public static CandidateContext Empty { get; } = new(null, null, null);

    public bool IsEmpty => string.IsNullOrWhiteSpace(CvText) && string.IsNullOrWhiteSpace(JobDescription);

    public CandidateContext WithCv(string? cvText) => this with { CvText = cvText };
    public CandidateContext WithJobDescription(string? jd) => this with { JobDescription = jd };
}
