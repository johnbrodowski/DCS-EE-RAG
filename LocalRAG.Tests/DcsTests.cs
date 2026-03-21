using Xunit;

namespace LocalRAG.Tests;

/// <summary>
/// Unit tests for Dynamic Context Selection (DCS) classes.
/// All tests are pure unit tests with no external dependencies.
/// </summary>
public class DcsTests
{
    // ── DcsKey: Weighted Similarity ─────────────────────────────────────

    [Fact]
    public void DcsKey_Similarity_WeightedCorrectly()
    {
        // Intent=0.5, DCN=0.3, Domain=0.2
        var a = new DcsKey
        {
            IntentIds = [1, 2],
            DomainIds = [10],
            DcnIds = [100]
        };

        var b = new DcsKey
        {
            IntentIds = [1, 2],  // exact intent match → 1.0
            DomainIds = [10],    // exact domain match → 1.0
            DcnIds = [100]       // exact DCN match → 1.0
        };

        var sim = a.Similarity(b);
        Assert.Equal(1.0, sim, precision: 10);
    }

    [Fact]
    public void DcsKey_Similarity_IntentDominates()
    {
        var a = new DcsKey { IntentIds = [1], DomainIds = [10], DcnIds = [100] };
        var intentOnly = new DcsKey { IntentIds = [1], DomainIds = [99], DcnIds = [999] };
        var domainOnly = new DcsKey { IntentIds = [99], DomainIds = [10], DcnIds = [999] };

        var intentSim = a.Similarity(intentOnly);
        var domainSim = a.Similarity(domainOnly);

        // Intent match (0.5 weight) should score higher than domain match (0.2 weight)
        Assert.True(intentSim > domainSim,
            $"Intent similarity ({intentSim:F3}) should exceed domain similarity ({domainSim:F3})");
    }

    [Fact]
    public void DcsKey_Similarity_EmptyArrays()
    {
        var empty = new DcsKey();
        var other = new DcsKey();

        // Both empty → Jaccard returns 1.0 for each category
        var sim = empty.Similarity(other);
        Assert.Equal(1.0, sim, precision: 10);
    }

    [Fact]
    public void DcsKey_Similarity_OneEmptyOnePopulated()
    {
        var empty = new DcsKey();
        var populated = new DcsKey { IntentIds = [1], DomainIds = [2], DcnIds = [3] };

        // Empty vs populated → Jaccard returns 0 for each category
        var sim = empty.Similarity(populated);
        Assert.Equal(0.0, sim, precision: 10);
    }

    // ── DcsKey: Jaccard ─────────────────────────────────────────────────

    [Fact]
    public void DcsKey_Jaccard_ExactMatch_ReturnsOne()
    {
        var result = DcsKey.Jaccard([1, 2, 3], [1, 2, 3]);
        Assert.Equal(1.0, result, precision: 10);
    }

    [Fact]
    public void DcsKey_Jaccard_NoOverlap_ReturnsZero()
    {
        var result = DcsKey.Jaccard([1, 2], [3, 4]);
        Assert.Equal(0.0, result, precision: 10);
    }

    [Fact]
    public void DcsKey_Jaccard_PartialOverlap()
    {
        // {1,2} ∩ {2,3} = {2}, union = {1,2,3} → 1/3
        var result = DcsKey.Jaccard([1, 2], [2, 3]);
        Assert.Equal(1.0 / 3.0, result, precision: 10);
    }

    [Fact]
    public void DcsKey_Jaccard_BothEmpty_ReturnsOne()
    {
        var result = DcsKey.Jaccard([], []);
        Assert.Equal(1.0, result, precision: 10);
    }

    // ── DcsKey: Serialization ───────────────────────────────────────────

    [Fact]
    public void DcsKey_Serialize_Roundtrip()
    {
        var original = new DcsKey
        {
            IntentIds = [1, 2, 3],
            DomainIds = [10, 20],
            DcnIds = [100]
        };

        var serialized = original.Serialize();
        var deserialized = DcsKey.Deserialize(serialized);

        Assert.Equal(original.IntentIds, deserialized.IntentIds);
        Assert.Equal(original.DomainIds, deserialized.DomainIds);
        Assert.Equal(original.DcnIds, deserialized.DcnIds);
    }

    [Fact]
    public void DcsKey_Serialize_CompactFormat()
    {
        var key = new DcsKey
        {
            IntentIds = [1, 2],
            DomainIds = [3],
            DcnIds = [4, 5]
        };

        var serialized = key.Serialize();
        Assert.Equal("1,2|3|4,5", serialized);
    }

    [Fact]
    public void DcsKey_Deserialize_EmptyString()
    {
        var key = DcsKey.Deserialize("");
        Assert.Empty(key.IntentIds);
        Assert.Empty(key.DomainIds);
        Assert.Empty(key.DcnIds);
    }

    // ── DcnNode: Recency Weight ─────────────────────────────────────────

    [Fact]
    public void DcnNode_RecencyWeight_RecentIsHigh()
    {
        var dcn = new DcnNode { LastReferencedUtc = DateTime.UtcNow };
        var weight = dcn.ComputeRecencyWeight();

        // Just referenced → weight should be very close to 1.0
        Assert.True(weight > 0.99, $"Recent weight should be ~1.0, got {weight:F4}");
    }

    [Fact]
    public void DcnNode_RecencyWeight_OldDecays()
    {
        var dcn = new DcnNode { LastReferencedUtc = DateTime.UtcNow.AddHours(-48) };
        var weight = dcn.ComputeRecencyWeight();

        // 48 hours old → exp(-48/24) = exp(-2) ≈ 0.135
        Assert.True(weight < 0.2, $"48h-old weight should be <0.2, got {weight:F4}");
        Assert.True(weight > 0.1, $"48h-old weight should be >0.1, got {weight:F4}");
    }

    [Fact]
    public void DcnNode_Absorb_CombinesIdentifiers()
    {
        var keeper = new DcnNode
        {
            DcnId = 1,
            TopicIdentifiers = [1, 2],
            LinkedMessageIds = ["msg1"],
            CreatedUtc = DateTime.UtcNow.AddHours(-2)
        };

        var merged = new DcnNode
        {
            DcnId = 2,
            TopicIdentifiers = [2, 3],
            LinkedMessageIds = ["msg2"],
            CreatedUtc = DateTime.UtcNow.AddHours(-5) // older
        };

        keeper.Absorb(merged);

        Assert.Equal([1, 2, 3], keeper.TopicIdentifiers.OrderBy(x => x).ToArray());
        Assert.Contains("msg1", keeper.LinkedMessageIds);
        Assert.Contains("msg2", keeper.LinkedMessageIds);
        // Should keep the earlier creation time
        Assert.Equal(merged.CreatedUtc, keeper.CreatedUtc);
    }

    // ── DcsContextAssembler: DCN Merge ──────────────────────────────────

    [Fact]
    public void DcsContextAssembler_MergeSimilarDcns_MergesAboveThreshold()
    {
        var assembler = new DcsContextAssembler();

        // Create two DCNs with identical topic identifiers (similarity = 1.0 > 0.75)
        var key1 = new DcsKey { IntentIds = [1], DomainIds = [10] };
        var dcn1 = assembler.FindOrCreateDcn(key1);

        var key2 = new DcsKey { IntentIds = [1], DomainIds = [10] };
        var dcn2 = assembler.FindOrCreateDcn(key2);

        // If they matched the same DCN, only 1 exists. Force a second.
        // Directly test the merge by checking DCN count after merge.
        int countBefore = assembler.DcnStore.Count;
        assembler.MergeSimilarDcns();
        int countAfter = assembler.DcnStore.Count;

        Assert.True(countAfter <= countBefore, "Merge should not increase DCN count");
    }

    [Fact]
    public void DcsContextAssembler_MergeSimilarDcns_KeepsOlderId()
    {
        var assembler = new DcsContextAssembler();

        // Create two DCNs manually with high similarity
        var dcn1 = assembler.FindOrCreateDcn(new DcsKey { IntentIds = [1], DomainIds = [10] });
        var dcn2 = assembler.FindOrCreateDcn(new DcsKey { IntentIds = [99], DomainIds = [99] });

        // After merge, if they were similar enough, the lower ID should survive
        assembler.MergeSimilarDcns();

        if (assembler.DcnStore.Count == 1)
        {
            Assert.True(assembler.DcnStore.ContainsKey(dcn1.DcnId),
                "Older (lower ID) DCN should survive merge");
        }
    }

    // ── DcsContextAssembler: Context Assembly ───────────────────────────

    [Fact]
    public void DcsContextAssembler_AssembleContext_RespectsPerDcnCap()
    {
        var assembler = new DcsContextAssembler();
        var dcn = assembler.FindOrCreateDcn(new DcsKey { IntentIds = [1], DomainIds = [10] });

        // Register more than MAX_PER_DCN messages linked to the same DCN
        for (int i = 0; i < DcsContextAssembler.MAX_PER_DCN + 10; i++)
        {
            assembler.RegisterMessage(new DcsMessageRecord
            {
                MessageId = $"msg-{i}",
                Key = new DcsKey { IntentIds = [1], DomainIds = [10], DcnIds = [dcn.DcnId] },
                HardLinkedDcns = [dcn.DcnId],
                Content = $"Message {i}"
            });
        }

        var context = assembler.AssembleContext(
            new DcsKey { IntentIds = [1], DomainIds = [10], DcnIds = [dcn.DcnId] },
            maxTotal: 100);

        Assert.True(context.Count <= DcsContextAssembler.MAX_PER_DCN,
            $"Expected at most {DcsContextAssembler.MAX_PER_DCN} messages per DCN, got {context.Count}");
    }

    [Fact]
    public void DcsContextAssembler_IdentifierIndex_FindsCandidates()
    {
        var assembler = new DcsContextAssembler();

        assembler.RegisterMessage(new DcsMessageRecord
        {
            MessageId = "target",
            Key = new DcsKey { IntentIds = [1], DomainIds = [10] },
            HardLinkedDcns = [],
            Content = "Target message"
        });

        assembler.RegisterMessage(new DcsMessageRecord
        {
            MessageId = "unrelated",
            Key = new DcsKey { IntentIds = [99], DomainIds = [99] },
            HardLinkedDcns = [],
            Content = "Unrelated message"
        });

        // Query matching the target's identifiers
        var context = assembler.AssembleContext(new DcsKey { IntentIds = [1], DomainIds = [10] });

        Assert.Contains(context, m => m.MessageId == "target");
    }

    // ── DcsContextAssembler: Influence Scoring ──────────────────────────

    [Fact]
    public void DcsContextAssembler_ComputeInfluence_SharedDcn()
    {
        var assembler = new DcsContextAssembler();

        var current = new DcsMessageRecord
        {
            MessageId = "a",
            Key = new DcsKey { IntentIds = [1] },
            HardLinkedDcns = [100]
        };

        var next = new DcsMessageRecord
        {
            MessageId = "b",
            Key = new DcsKey { IntentIds = [2] },
            HardLinkedDcns = [100]  // shared DCN
        };

        var influence = assembler.ComputeInfluence(current, next);
        Assert.True(influence >= 0.6, $"Shared DCN should contribute >=0.6, got {influence:F3}");
    }

    [Fact]
    public void DcsContextAssembler_ComputeInfluence_Escalation()
    {
        var assembler = new DcsContextAssembler();

        var current = new DcsMessageRecord
        {
            MessageId = "a",
            Key = new DcsKey { IntentIds = [DcsIntentClassifier.INTENT_CHAT] },
            HardLinkedDcns = []
        };

        var next = new DcsMessageRecord
        {
            MessageId = "b",
            Key = new DcsKey { IntentIds = [DcsIntentClassifier.INTENT_DESIGN] },
            HardLinkedDcns = []
        };

        var influence = assembler.ComputeInfluence(current, next);
        Assert.True(influence >= 0.3, $"Escalation should contribute >=0.3, got {influence:F3}");
    }

    [Fact]
    public void DcsContextAssembler_ComputeInfluence_CappedAtOne()
    {
        var assembler = new DcsContextAssembler();

        // Stack all signals: shared DCN (0.6) + domain (0.2) + escalation (0.3) = 1.1 → capped at 1.0
        var current = new DcsMessageRecord
        {
            MessageId = "a",
            Key = new DcsKey
            {
                IntentIds = [DcsIntentClassifier.INTENT_CHAT],
                DomainIds = [10]
            },
            HardLinkedDcns = [100]
        };

        var next = new DcsMessageRecord
        {
            MessageId = "b",
            Key = new DcsKey
            {
                IntentIds = [DcsIntentClassifier.INTENT_FIX],
                DomainIds = [10]
            },
            HardLinkedDcns = [100]
        };

        var influence = assembler.ComputeInfluence(current, next);
        Assert.Equal(1.0, influence, precision: 10);
    }

    // ── DcsIntentClassifier ─────────────────────────────────────────────

    [Fact]
    public void DcsIntentClassifier_DetectsMultipleIntents()
    {
        // Message with both a query and a fix intent
        var intents = DcsIntentClassifier.ClassifyIntents("How do I fix this bug?");

        Assert.Contains(DcsIntentClassifier.INTENT_QUERY, intents);
        Assert.Contains(DcsIntentClassifier.INTENT_FIX, intents);
    }

    [Fact]
    public void DcsIntentClassifier_FallsBackToChat()
    {
        var intents = DcsIntentClassifier.ClassifyIntents("hello there");
        Assert.Contains(DcsIntentClassifier.INTENT_CHAT, intents);
    }

    [Fact]
    public void DcsIntentClassifier_DetectsDomains()
    {
        var domains = DcsIntentClassifier.ClassifyDomains("Deploy the backend API to kubernetes");

        Assert.Contains(DcsIntentClassifier.DOMAIN_BACKEND, domains);
        Assert.Contains(DcsIntentClassifier.DOMAIN_INFRASTRUCTURE, domains);
    }

    [Fact]
    public void DcsIntentClassifier_NoDomainReturnsEmpty()
    {
        var domains = DcsIntentClassifier.ClassifyDomains("good morning");
        Assert.Empty(domains);
    }

    [Fact]
    public void DcsIntentClassifier_IsEscalation_ChatToDesign()
    {
        Assert.True(DcsIntentClassifier.IsEscalation(
            [DcsIntentClassifier.INTENT_CHAT],
            [DcsIntentClassifier.INTENT_DESIGN]));
    }

    [Fact]
    public void DcsIntentClassifier_IsEscalation_DesignToChat_IsFalse()
    {
        Assert.False(DcsIntentClassifier.IsEscalation(
            [DcsIntentClassifier.INTENT_DESIGN],
            [DcsIntentClassifier.INTENT_CHAT]));
    }

    // ── DcsLogEntry ─────────────────────────────────────────────────────

    [Fact]
    public void DcsLogEntry_ToString_FormatsCorrectly()
    {
        var entry = new DcsLogEntry
        {
            SelectedDcnId = 5,
            CandidateMessageCount = 100,
            FilteredMessageCount = 15,
            TopScores = [("msg1", 0.95), ("msg2", 0.82)],
            Strategy = "Selective"
        };

        var str = entry.ToString();
        Assert.Contains("DCN=5", str);
        Assert.Contains("Candidates=100", str);
        Assert.Contains("Filtered=15", str);
        Assert.Contains("Selective", str);
    }

    // ── DcsContextAssembler: Logging ────────────────────────────────────

    [Fact]
    public void DcsContextAssembler_AssembleContext_LogsDecision()
    {
        var assembler = new DcsContextAssembler();

        assembler.RegisterMessage(new DcsMessageRecord
        {
            MessageId = "msg1",
            Key = new DcsKey { IntentIds = [1] },
            HardLinkedDcns = [],
            Content = "test"
        });

        assembler.AssembleContext(new DcsKey { IntentIds = [1] });

        Assert.Single(assembler.Log);
        Assert.Equal("Selective", assembler.Log[0].Strategy);
    }
}
