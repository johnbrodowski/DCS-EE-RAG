using Xunit;

namespace LocalRAG.Tests;

/// <summary>
/// AssembleContext scores similarity x DCN recency. These pin that the decay is actually
/// applied (it used to be clamped to 1.0 by Math.Max against a 1.0 seed).
/// </summary>
public class DcsDecayTests
{
    private static DcsKey Key() => new() { IntentIds = [1], DomainIds = [2] };

    [Fact]
    public void AssembleContext_RanksFreshDcnAboveStaleDcn_ForEqualSimilarity()
    {
        var dcs = new DcsContextAssembler();
        var stale = dcs.FindOrCreateDcn(new DcsKey { IntentIds = [1], DomainIds = [2] });
        var fresh = dcs.FindOrCreateDcn(new DcsKey { IntentIds = [50], DomainIds = [60] });
        Assert.NotEqual(stale.DcnId, fresh.DcnId);

        // Stale message registered first so a tie (the old behaviour) would list it first.
        dcs.RegisterMessage(new DcsMessageRecord { MessageId = "stale", Key = Key(), HardLinkedDcns = [stale.DcnId] });
        dcs.RegisterMessage(new DcsMessageRecord { MessageId = "fresh", Key = Key(), HardLinkedDcns = [fresh.DcnId] });

        // RegisterMessage touches the DCN, so age it afterwards.
        stale.LastReferencedUtc = DateTime.UtcNow.AddHours(-72);

        var ids = dcs.AssembleContext(Key()).Select(m => m.MessageId).ToList();
        Assert.Equal(new[] { "fresh", "stale" }, ids);
    }

    [Fact]
    public void AssembleContext_UsesFreshestOfSeveralLinkedDcns()
    {
        var dcs = new DcsContextAssembler();
        var old = dcs.FindOrCreateDcn(new DcsKey { IntentIds = [1], DomainIds = [2] });
        var recent = dcs.FindOrCreateDcn(new DcsKey { IntentIds = [50], DomainIds = [60] });
        var other = dcs.FindOrCreateDcn(new DcsKey { IntentIds = [70], DomainIds = [80] });

        dcs.RegisterMessage(new DcsMessageRecord { MessageId = "old-only", Key = Key(), HardLinkedDcns = [old.DcnId] });
        dcs.RegisterMessage(new DcsMessageRecord { MessageId = "both", Key = Key(), HardLinkedDcns = [old.DcnId, recent.DcnId] });
        dcs.RegisterMessage(new DcsMessageRecord { MessageId = "other-old", Key = Key(), HardLinkedDcns = [other.DcnId] });

        old.LastReferencedUtc = DateTime.UtcNow.AddHours(-72);
        other.LastReferencedUtc = DateTime.UtcNow.AddHours(-48);

        var ids = dcs.AssembleContext(Key()).Select(m => m.MessageId).ToList();
        Assert.Equal("both", ids[0]);                    // freshest linked DCN wins
        Assert.True(ids.IndexOf("other-old") < ids.IndexOf("old-only")); // 48h beats 72h
    }

    [Fact]
    public void AssembleContext_MessageWithoutKnownDcn_IsNotDecayed()
    {
        var dcs = new DcsContextAssembler();
        var stale = dcs.FindOrCreateDcn(new DcsKey { IntentIds = [1], DomainIds = [2] });

        dcs.RegisterMessage(new DcsMessageRecord { MessageId = "stale", Key = Key(), HardLinkedDcns = [stale.DcnId] });
        dcs.RegisterMessage(new DcsMessageRecord { MessageId = "unlinked", Key = Key() });
        stale.LastReferencedUtc = DateTime.UtcNow.AddHours(-72);

        var ids = dcs.AssembleContext(Key()).Select(m => m.MessageId).ToList();
        Assert.Equal(new[] { "unlinked", "stale" }, ids);
    }
}
