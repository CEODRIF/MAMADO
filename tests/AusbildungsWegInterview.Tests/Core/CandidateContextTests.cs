using AusbildungsWegInterview.Core.Domain;
using Xunit;

namespace AusbildungsWegInterview.Tests.Core;

public class CandidateContextTests
{
    [Fact]
    public void Empty_WhenNoMaterial()
        => Assert.True(CandidateContext.Empty.IsEmpty);

    [Fact]
    public void NotEmpty_WithCv()
    {
        var c = CandidateContext.Empty.WithCv("experience");
        Assert.False(c.IsEmpty);
        Assert.Equal("experience", c.CvText);
    }

    [Fact]
    public void WithJobDescription_SetsField()
    {
        var c = CandidateContext.Empty.WithJobDescription("developer role");
        Assert.Equal("developer role", c.JobDescription);
    }
}
