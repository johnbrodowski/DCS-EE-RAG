using Xunit;

namespace LocalRAG.Tests;

/// <summary>
/// Intent, domain and DCN ids all start at 1. These cases use small, overlapping ids on purpose (the older
/// DcsTests use 100/50/60, which hide the collision), and cover the injectable clock and malformed stored keys.
/// </summary>
public class DcsNamespaceTests
{
    private static DcsMessageRecord Msg(string id, int[] intent, int[] domain) =>
        new() { MessageId = id, Key = new DcsKey { IntentIds = intent, DomainIds = domain }, Content = id };

    [Fact]
    public void Index_DoesNotMixIntentDomainAndDcnIds()
    {
        var dcs = new DcsContextAssembler();
        dcs.RegisterMessage(Msg("collides", intent: [5], domain: [1]));   // domain 1 == the query's intent 1 as a bare number
        dcs.RegisterMessage(Msg("matches", intent: [1], domain: [3]));

        var result = dcs.AssembleContext(new DcsKey { IntentIds = [1], DomainIds = [3] });

        Assert.Equal(new[] { "matches" }, result.Select(m => m.MessageId));
    }

    [Fact]
    public void Similarity_WithoutDcn_IsDecidedByIntentAndDomain()
    {
        var none = new DcsKey { IntentIds = [1], DomainIds = [3] };
        Assert.Equal(0.0, none.Similarity(new DcsKey { IntentIds = [5], DomainIds = [1] }), precision: 10);
        Assert.Equal(1.0, none.Similarity(new DcsKey { IntentIds = [1], DomainIds = [3] }), precision: 10);
    }

    [Fact]
    public void Similarity_DcnOnOneSideOnly_DoesNotChangeTheScore()
    {
        var message = new DcsKey { IntentIds = [1], DomainIds = [3] };
        var withDcn = new DcsKey { IntentIds = [1], DomainIds = [3], DcnIds = [2] };
        var withoutDcn = new DcsKey { IntentIds = [1], DomainIds = [3] };

        Assert.Equal(message.Similarity(withoutDcn), message.Similarity(withDcn), precision: 10);
    }

    [Fact]
    public void Similarity_BothNameADcn_StillUsesIt()
    {
        var a = new DcsKey { IntentIds = [1], DomainIds = [3], DcnIds = [2] };
        var sameThread = new DcsKey { IntentIds = [1], DomainIds = [3], DcnIds = [2] };
        var otherThread = new DcsKey { IntentIds = [1], DomainIds = [3], DcnIds = [9] };

        Assert.Equal(1.0, a.Similarity(sameThread), precision: 10);
        Assert.Equal(0.7, a.Similarity(otherThread), precision: 10);
    }

    [Fact]
    public void Clock_StampsDcnsAndDrivesRecency()
    {
        var now = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        var dcs = new DcsContextAssembler(() => now);

        var dcn = dcs.FindOrCreateDcn(new DcsKey { IntentIds = [1], DomainIds = [3] });
        Assert.Equal(now, dcn.CreatedUtc);
        Assert.Equal(now, dcn.LastReferencedUtc);

        Assert.Equal(1.0, dcn.ComputeRecencyWeight(now), precision: 10);
        Assert.Equal(Math.Exp(-1), dcn.ComputeRecencyWeight(now.AddHours(24)), precision: 10);

        now = now.AddHours(5);
        dcs.RegisterMessage(new DcsMessageRecord { MessageId = "m", Key = new DcsKey { IntentIds = [1], DomainIds = [3] }, HardLinkedDcns = [dcn.DcnId] });
        Assert.Equal(now, dcn.LastReferencedUtc);
    }

    [Fact]
    public void Deserialize_MalformedKey_DoesNotThrow()
    {
        var key = DcsKey.Deserialize("1,x,2|99999999999|-3, 4");
        Assert.Equal(new[] { 1, 2 }, key.IntentIds);
        Assert.Empty(key.DomainIds);
        Assert.Equal(new[] { 4 }, key.DcnIds);
    }
}
