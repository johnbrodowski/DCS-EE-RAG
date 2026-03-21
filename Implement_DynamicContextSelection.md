# DCS Improvement Report — Required Changes for Production Readiness

**Target:** Dynamic Context Selection (DCS) in EE-RAG
**Purpose:** Improve correctness, stability, and scalability of the current implementation spec without changing the overall architecture.

---

## 1. Replace Raw Jaccard with Weighted Similarity

### Problem

Current similarity treats all identifiers equally:

```csharp
intersection / union
```

This causes:

* Intent mismatches to be under-penalized
* DCN matches to be under-weighted
* Noisy matches when keys grow

---

### Required Change

Split identifiers into categories and weight them:

#### Update `DcsKey`:

```csharp
public class DcsKey
{
    public int[] IntentIds { get; set; } = Array.Empty<int>();
    public int[] DomainIds { get; set; } = Array.Empty<int>();
    public int[] DcnIds { get; set; } = Array.Empty<int>();

    public double Similarity(DcsKey other)
    {
        double intentScore = Jaccard(IntentIds, other.IntentIds);
        double domainScore = Jaccard(DomainIds, other.DomainIds);
        double dcnScore    = Jaccard(DcnIds, other.DcnIds);

        return (intentScore * 0.5) +
               (domainScore * 0.2) +
               (dcnScore * 0.3);
    }

    private double Jaccard(int[] a, int[] b)
    {
        if (a.Length == 0 && b.Length == 0) return 1.0;
        if (a.Length == 0 || b.Length == 0) return 0.0;

        var setA = new HashSet<int>(a);
        var setB = new HashSet<int>(b);

        int intersection = setA.Count(x => setB.Contains(x));
        int union = setA.Count + setB.Count - intersection;

        return union == 0 ? 0.0 : (double)intersection / union;
    }
}
```

---

### Notes

* Intent should dominate matching
* DCN match is critical for continuity
* Domain is secondary signal

---

## 2. Add DCN Merge Logic (Prevent Topic Fragmentation)

### Problem

High match thresholds create too many DCNs.

---

### Required Change

Add a merge step in `DcsContextAssembler`:

```csharp
private void MergeSimilarDcns(List<DcnNode> dcns)
{
    for (int i = 0; i < dcns.Count; i++)
    {
        for (int j = i + 1; j < dcns.Count; j++)
        {
            var sim = ComputeTopicSimilarity(dcns[i], dcns[j]);

            if (sim >= 0.75)
            {
                Merge(dcns[i], dcns[j]);
            }
        }
    }
}
```

#### Merge behavior:

* Combine `TopicIdentifiers`
* Reassign all message references
* Keep older DCN ID
* Delete merged node

---

### Add helper:

```csharp
private double ComputeTopicSimilarity(DcnNode a, DcnNode b)
{
    var keyA = new DcsKey { DomainIds = a.TopicIdentifiers };
    var keyB = new DcsKey { DomainIds = b.TopicIdentifiers };

    return keyA.Similarity(keyB);
}
```

---

## 3. Replace Heuristic Soft Link Scoring with Behavioral Signals

### Problem

Current scoring is static and content-based → unreliable.

---

### Required Change

Track **influence via conversation flow**:

#### Update scoring logic:

```csharp
double ComputeInfluence(DcsMessageRecord current, DcsMessageRecord next)
{
    double score = 0.0;

    // If next message shares DCN → strong influence
    if (current.HardLinkedDcns.Intersect(next.HardLinkedDcns).Any())
        score += 0.6;

    // If identifiers propagate forward
    if (current.Key.DomainIds.Intersect(next.Key.DomainIds).Any())
        score += 0.2;

    // If intent escalates (CHAT → DESIGN/FIX)
    if (IsEscalation(current.Key.IntentIds, next.Key.IntentIds))
        score += 0.3;

    return Math.Min(score, 1.0);
}
```

---

### Additional requirement

When saving a message:

* Compare it to **next 1–2 messages**
* Update influence retroactively

---

## 4. Introduce Identifier Index (Avoid Full Scan)

### Problem

Current approach scans all messages → O(n)

---

### Required Change

Maintain in-memory index:

```csharp
Dictionary<int, List<string>> IdentifierIndex;
```

---

### On message insert:

```csharp
foreach (var id in message.Key.AllIdentifiers())
{
    if (!IdentifierIndex.ContainsKey(id))
        IdentifierIndex[id] = new List<string>();

    IdentifierIndex[id].Add(message.MessageId);
}
```

---

### On retrieval:

```csharp
HashSet<string> candidateIds = new();

foreach (var id in candidateKey.AllIdentifiers())
{
    if (IdentifierIndex.TryGetValue(id, out var ids))
    {
        foreach (var msgId in ids)
            candidateIds.Add(msgId);
    }
}
```

Then only compute similarity on `candidateIds`.

---

## 5. Modify Function Hash Behavior (Avoid Incorrect Reuse)

### Problem

Current logic replaces generation → unsafe.

---

### Required Change

Change behavior:

#### OLD:

> Replace generation with stored version

#### NEW:

> Surface as suggestion

---

### Implementation

```csharp
var existing = FindByFunctionHash(hash);

if (existing != null)
{
    AddToContext("Previously implemented function:\n" + existing.Code);
}
```

DO NOT skip generation.

---

## 6. Add DCN Activity Decay (Prevent Stale Topic Bias)

### Problem

Old DCNs remain equally weighted forever.

---

### Required Change

Apply time-based decay:

```csharp
double ComputeRecencyWeight(DcnNode dcn)
{
    var age = DateTime.UtcNow - dcn.LastReferencedUtc;
    return Math.Exp(-age.TotalHours / 24); // 24h half-life
}
```

---

### Apply during DCN matching:

```csharp
score *= ComputeRecencyWeight(dcn);
```

---

## 7. Improve Multi-Intent Support

### Problem

Messages often contain multiple intents, but system forces one.

---

### Required Change

Allow multiple intent IDs:

```csharp
public int[] IntentIds { get; set; }
```

---

### Update classifier:

* Detect multiple patterns
* Assign multiple intents

---

### Update similarity:

Already supported via Jaccard/weighted logic

---

## 8. Optimize Storage Format (Reduce JSON Overhead)

### Problem

JSON parsing for every lookup is wasteful.

---

### Required Change (optional but recommended)

Store identifiers as:

```
"1,20,42"
```

Instead of:

```
"[1,20,42]"
```

---

### Add parser:

```csharp
int[] ParseIds(string s)
{
    return s.Split(',').Select(int.Parse).ToArray();
}
```

---

## 9. Add Hard Limit per DCN (Prevent Context Flooding)

### Problem

A single DCN can dominate context.

---

### Required Change

Cap messages per DCN:

```csharp
const int MAX_PER_DCN = 20;
```

---

### Apply during assembly:

* Group by DCN
* Take most recent N per group

---

## 10. Add Logging Hooks (Critical for Tuning)

### Required Change

Log:

```csharp
Log:
- Selected DCN
- Number of candidate messages
- Messages after filtering
- Similarity scores (top 5)
- Strategy used (Full vs Selective)
```

---

### Purpose

You will need this to:

* tune thresholds
* debug misclassification
* validate behavior

---

# Summary of Changes

| Area           | Change                                |
| -------------- | ------------------------------------- |
| Similarity     | Replace Jaccard with weighted scoring |
| DCNs           | Add merge logic + decay               |
| Soft Links     | Switch to behavioral scoring          |
| Performance    | Add identifier index                  |
| Function reuse | Suggest, don’t replace                |
| Classification | Support multi-intent                  |
| Storage        | Optional non-JSON format              |
| Context        | Cap per DCN                           |
| Observability  | Add logging                           |

---

# Final Instruction to Implementer

Do **not** change:

* overall architecture
* pipeline order
* EE-RAG integration

These changes are strictly:

> improving correctness, stability, and scalability of the existing design
