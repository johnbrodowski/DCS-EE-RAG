using Xunit;

namespace LocalRAG.Tests;

/// <summary>
/// The DCN term of DcsKey.Similarity (0.3) only discriminates when the query key carries
/// a DCN id. The pipeline resolves one with FindMatchingDcn before assembling context.
/// </summary>
public class DcsContinuityTests
{
    [Fact]
    public void FindMatchingDcn_ReturnsBestMatch_WithoutCreatingOrTouching()
    {
        var dcs = new DcsContextAssembler();
        Assert.Null(dcs.FindMatchingDcn(new DcsKey { IntentIds = [1], DomainIds = [2] })); // empty store
        Assert.Empty(dcs.DcnStore);

        var thread = dcs.FindOrCreateDcn(new DcsKey { IntentIds = [1], DomainIds = [2] });
        var other = dcs.FindOrCreateDcn(new DcsKey { IntentIds = [40], DomainIds = [41] });
        var stamp = DateTime.UtcNow.AddHours(-1);
        thread.LastReferencedUtc = stamp;

        var found = dcs.FindMatchingDcn(new DcsKey { IntentIds = [1], DomainIds = [2] });

        Assert.Same(thread, found);
        Assert.Equal(2, dcs.DcnStore.Count);                 // nothing created
        Assert.Equal(stamp, thread.LastReferencedUtc);       // nothing touched
        Assert.NotSame(other, found);
        Assert.Null(dcs.FindMatchingDcn(new DcsKey { IntentIds = [900], DomainIds = [901] })); // no match -> no DCN
        Assert.Equal(2, dcs.DcnStore.Count);
    }

    [Fact]
    public void QueryWithDcnId_PrefersSameThreadMessage_WhereQueryWithoutOneDoesNot()
    {
        var dcs = new DcsContextAssembler();
        var thread = dcs.FindOrCreateDcn(new DcsKey { IntentIds = [1], DomainIds = [2] });
        var other = dcs.FindOrCreateDcn(new DcsKey { IntentIds = [40], DomainIds = [41] });

        // "outside" matches intent+domain exactly but is in another thread;
        // "inside" is a weaker topical match in the query's own thread.
        dcs.RegisterMessage(new DcsMessageRecord
        {
            MessageId = "outside",
            Key = new DcsKey { IntentIds = [1], DomainIds = [2], DcnIds = [other.DcnId] },
            HardLinkedDcns = [other.DcnId]
        });
        dcs.RegisterMessage(new DcsMessageRecord
        {
            MessageId = "inside",
            Key = new DcsKey { IntentIds = [1], DomainIds = [2, 3], DcnIds = [thread.DcnId] },
            HardLinkedDcns = [thread.DcnId]
        });

        var plain = new DcsKey { IntentIds = [1], DomainIds = [2] };
        Assert.Equal("outside", dcs.AssembleContext(plain).First().MessageId); // continuity term constant

        var resolved = new DcsKey { IntentIds = [1], DomainIds = [2] };
        resolved.DcnIds = [dcs.FindMatchingDcn(resolved)!.DcnId];
        Assert.Equal("inside", dcs.AssembleContext(resolved).First().MessageId);
    }
}
