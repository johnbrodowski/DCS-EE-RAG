Author: John Brodowski — Published: March 7, 2026 — Rev 4: March 9, 2026

Abstract

Retrieval-Augmented Generation (RAG) grounds large language model (LLM) responses in external knowledge. However, naive RAG implementations share structural flaws that degrade model performance, inflate token use, and corrupt conversation history. We introduce Elective Ephemeral RAG (EE-RAG), a pipeline of five coordinated principles: summary-first retrieval, model-elected expansion, transient injection, history isolation, and silent digestion. EE-RAG makes retrieval invisible infrastructure: the model sees only what it needs, when it needs it, with no artifacts left behind.

1. Introduction

Standard RAG was designed to address two LLM limitations: fixed training data and limited context windows. By retrieving relevant documents at inference and injecting them into the prompt, RAG lets models reason over up-to-date data. However, the common practice — retrieve top-k chunks (256–512 tokens each), concatenate them, then generate — embeds unexamined assumptions:

Retrieved chunks are treated as relevant simply if they meet a similarity threshold.
The model gets all retrieved content regardless of what it actually needs.
Retrieved text is added to conversation history as if it were user content.
The retrieval process is exposed to the model (e.g., the model is asked to cite sources explicitly).

These assumptions accumulate errors over multi-turn dialogs: the system becomes noisy, expensive, and incoherent. In this work, we challenge all four and add a fifth insight: the model may silently digest retrieved knowledge without it entering the prompt.

2. Problems with Naive RAG

Before detailing EE-RAG, we precisely define failures in conventional RAG.

2.1 Context Flooding

Context flooding occurs when too much retrieved content is injected. A typical RAG call retrieves 3–5 chunks (each ~256–512 tokens) and injects all of them unconditionally. In practice, this introduces "context confusion": irrelevant content competes for the model's attention, hurting accuracy. Shi et al. (2023) demonstrated that model performance is substantially decreased when irrelevant information is included in the context — models attend to noise rather than filtering it. Similarly, Liu et al. (2024) showed that LLM performance exhibits a U-shaped curve with respect to context position: performance is highest when relevant information occurs at the beginning or end of the input, and degrades significantly when relevant information is buried in the middle of long contexts. Together, these findings indicate that past a certain point, additional context tokens can harm rather than help.

The token cost is also significant. Injecting 3 chunks × 500 tokens = approximately 1,500 tokens of context — potentially none of which the model required — is a common baseline.

2.2 Assumed Relevance

Retrieval often relies on similarity scores, but semantic similarity is not the same as actual need. A chunk might be topically adjacent — high cosine similarity — yet irrelevant to the specific query in its current conversational context. In naive RAG, any chunk above threshold is injected blindly. This conflates "similar" with "needed," which are fundamentally different properties. Embedding-based search optimizes for semantic proximity; it cannot account for what has already been established in the conversation, or whether a passage will actually improve the model's response.

2.3 History Contamination

A structurally damaging flaw receives little attention: RAG content pollutes conversation history.

In most implementations, retrieved chunks are inserted into the persistent messages array as part of a system or user turn. When the assistant responds, the entire exchange — including injected RAG content — is saved into history. This leads to two problems:

Forward contamination: Retrieved text from turn N remains in turns N+1, N+2, and beyond, even if irrelevant. In long conversations, old retrievals can dominate the context window, crowding out the actual dialog.

Retrieval echo: If the conversation — including RAG injections — is later re-indexed or embedded, the system can end up retrieving its own past answers. In a memory-augmented system, the knowledge base progressively fills with synthetic content. Future queries may then surface these artifacts as if they were ground-truth facts, creating a feedback loop that degrades the index over time.

2.4 Retrieval Visibility

Many RAG setups force the model to display retrieval — for example, prompting it to "cite sources." While citation can be useful, it also compels the model to narrate its search process rather than simply using the information. The model ends up speaking about its memory access rather than answering. This verbosity is unnatural and inconsistent with how expert cognition works: an expert does not read a passage aloud; they internalize it and speak from knowledge.

3. EE-RAG Architecture

EE-RAG addresses each of the above failure modes through five coordinated layers. They operate on two categories of context: transient context, injected outside the persistent messages array for one inference call and never stored, and persistent history, carried forward into all subsequent turns. RAG content in EE-RAG is always transient. Only genuine user and assistant turns are persistent.

3.1 Layer 1: Summary-First Retrieval with Candidate Header

Instead of injecting full chunks, EE-RAG first presents summaries of candidates. When a query arrives, the system retrieves the top-k document chunks as usual, but then passes only truncated summaries — approximately 20–30 tokens each — to the model. These summaries function as index cards: enough to judge relevance, not enough to answer the question. Summaries are pre-generated at index time so that no additional latency is introduced at query time.

For example, if Entry 7 is about OAuth errors, its summary might read: "Entry 7: Authentication flow, OAuth2 token refresh sequence, error handling" (~12 tokens). The full chunk might cost 400 tokens. The model receives the summary and decides whether the full entry is needed.

These summaries are delivered under an explicit candidate header — a framing preamble that signals the nature of the content and the expected response behavior:

--- Potentially Relevant Context ---
The following entries were retrieved as candidates. Each is a summary.
To retrieve the full content of an entry, respond with: RETRIEVE <id>
(e.g. "RETRIEVE 7" or "RETRIEVE 3 7 12").
You are not required to retrieve any entry.
---

This framing prevents the model from treating candidate summaries as authoritative input and establishes the retrieval request protocol without embedding it in the system prompt or persistent history. The plaintext RETRIEVE command shown above is one implementation option; equivalent alternatives include JSON function calls or tool-call APIs. The architectural properties hold regardless of the mechanism chosen.

Estimated token comparison (illustrative, based on representative chunk sizes):

Naive RAG (3 chunks): ~3 × 500 tokens ≈ 1,500 tokens
EE-RAG summaries only (3 candidates): ~3 × 25 tokens ≈ 75 tokens
EE-RAG with one chunk elected: ~75 + 400 tokens ≈ 475 tokens

Based on these estimates, EE-RAG is expected to consume approximately 95% fewer tokens than naive RAG when no chunks are elected, and approximately 68% fewer when one chunk is elected. These figures are derived from representative chunk size assumptions and will vary in practice. Empirical benchmarking across representative workloads is planned as future work.

3.2 Layer 2: Model-Elected Context Expansion

After seeing the summary list, the model decides which entries — if any — to fetch. It may respond with:

RETRIEVE 7
RETRIEVE 3 7 12
Or, if none apply, answer directly without retrieving.

If the model requests one or more IDs, the system fetches exactly those full chunks from the knowledge base (KB). A second inference call is then made with those chunks provided as transient background context, outside the persistent history. If the model requests none, the first response is used directly and no full chunk retrieval occurs.

This inverts standard RAG's assumption. The model — having seen the full query and conversation history — is better positioned to judge relevance than the retrieval system, which operates without conversational context. In contrast, a pure retriever lacks that awareness and cannot account for what has already been established in the dialog.

If no IDs are relevant, the pipeline ends after the first inference: no extra tokens consumed, no irrelevant content in context.

3.3 Layer 3: Transient Injection with Hash References

When the model elects to retrieve a chunk, EE-RAG injects that content transiently. The chunk text is provided to the model for this one response and then discarded. It is not appended to conversation history and is not stored. To the conversation record, the retrieval never happened. The model's reply appears as if it knew the answer from its own knowledge.

This is the invisible infrastructure principle. Like a human expert glancing at a reference and then speaking without reading it aloud, EE-RAG grounds responses in external knowledge without exposing the lookup.

To maintain auditability, a small trace is recorded in the persistent history: an 8-character hexadecimal hash of each elected chunk's embedding vector. For example, after using chunk 7, the assistant turn receives the tag [RAG:a3f9b2c1]. This tag is deterministic — the same chunk always produces the same hash — and maps back to the exact document and embedding in the index. It costs approximately 4–6 tokens. Because it carries no semantic content, it cannot confuse the model in future turns, cannot surface as a false positive in future retrievals, and cannot pollute the embedding space if conversation history is itself indexed.

3.4 Two-Phase Audit (Message Record Annotation)

EE-RAG also distinguishes what to record before inference. When a user message is written to the conversation database, it is annotated in metadata with the IDs of all candidate summaries offered — prior to any model response. These IDs are metadata only; they do not enter the context. They log what the model was offered.

After inference, the assistant's message is annotated with the hashes of whichever chunks were actually used. The database then holds a complete audit trail: at query time, both what was offered and what was used are recorded.

Phase 1 (user message, pre-inference): record rag_candidates_surfaced = list of candidate IDs.
Phase 2 (assistant message, post-inference): record rag_entries_elected = list of chunk hashes.

This two-phase log provides full retrieval provenance — at a cost of metadata fields, not tokens.

3.5 Layer 4: Conversation History Isolation

Because retrieved text is never appended to the persistent messages array, conversation history remains clean. Only genuine user turns and assistant responses (with their hash tags) accumulate. This resolves both contamination modes from Section 2.3:

No forward contamination: Past retrievals never occupy future context. Each turn's persistent context is only the real dialog so far.
No retrieval echo: RAG content is never embedded or re-indexed. The KB contains only original knowledge. Future queries will not surface synthetic artifacts from prior retrievals.

In practice, this means long conversations can remain coherent. If the model has a 128k context window, EE-RAG ensures that nearly all of those tokens are meaningful dialog, not accumulated RAG snippets from prior turns.

3.6 Layer 5: Silent Digestion Mode (Optional)

EE-RAG optionally supports silent digestion. In standard operation, elected chunks are provided verbatim as transient background context. In silent mode, they are never sent directly to the model. Instead, the system produces a concise synthesis or distillation of the elected chunks, and only that synthesis is provided as background. Equivalently, the chunks may be processed in a separate prior inference call whose output is discarded after a distilled summary is extracted.

The result is that the final response is informed by retrieved knowledge but no raw retrieval content whatsoever appears in the inference context. This is appropriate when chunks are too sensitive or verbose to inject even transiently, or when the desired response style demands complete fluency with no structural trace of retrieval.

The tradeoff is fidelity. Standard ephemeral injection allows the model to reason directly over retrieved content, referencing specific details and structures. Silent digestion produces responses informed by retrieved knowledge but not anchored to its exact form. For factual recall tasks requiring precision, standard mode is preferred. For tasks where general orientation matters more than precise reproduction, silent digestion may be preferable.

Silent digestion is fully compatible with hash reference persistence and message record annotation. The audit trail is preserved regardless of which injection mode is used.

4. Pseudocode

Below is a sketch of the full EE-RAG pipeline. Implementation details — exact API calls, embedding models — are intentionally abstracted. The focus is on architectural steps.

```python
function EE_RAG_Pipeline(user_message, conversation_history, knowledge_base, silent_mode=False):

    # Step 1: Initial retrieval
    candidates = knowledge_base.retrieve(user_message, top_k=5)

    # Step 2: Build summary list (pre-generated at index time, ~25 tokens each)
    summary_list = []
    candidate_ids = []
    for each candidate in candidates:
        summary_list.append({ "id": candidate.id, "summary": candidate.summary })
        candidate_ids.append(candidate.id)

    # Step 3: Write user message to database FIRST — before inference
    # Candidate IDs saved as metadata only; never added to the prompt
    message_record = database.write({
        "role": "user",
        "content": user_message,
        "rag_candidates_surfaced": candidate_ids
    })

    # Step 4: Build transient background context (header + summaries)
    # NOT added to conversation_history (persistent messages array)
    background_context = format_candidate_header() + format_summary_list(summary_list)

    # Step 5: First inference — model evaluates summaries
    # 'background' is transient: provided at inference, never saved to history
    # e.g. a hidden system message, tool output, or custom context slot
    model_response = model.infer(
        messages = conversation_history + [user_message],
        background = background_context
    )

    # Step 6: Check if model elected to retrieve full chunk(s)
    # Retrieval request may be plaintext ("RETRIEVE 7") or a function/tool call
    if model_response.contains_retrieval_request():
        requested_ids = model_response.extract_requested_ids()

        # Step 7: Fetch only the elected chunks
        full_chunks = knowledge_base.fetch_by_id(requested_ids)

        if silent_mode:
            # Step 7a: Silent digestion
            # Chunks are distilled; raw content never enters the inference context
            synthesis = synthesize(full_chunks)  # compact distillation, discarded after use
            final_response = model.infer(
                messages = conversation_history + [user_message],
                background = synthesis
            )
        else:
            # Step 7b: Standard ephemeral injection
            # Full chunks as transient background — NOT added to conversation_history
            # Note: summaries are omitted here; the model has already evaluated them
            final_response = model.infer(
                messages = conversation_history + [user_message],
                background = full_chunks
            )

        # Generate 8-hex hash for each elected chunk embedding
        elected_hashes = [generate_hash(chunk.embedding) for chunk in full_chunks]
    else:
        # No retrieval requested; first response is final
        final_response = model_response
        elected_hashes = []

    # Step 8: Append only clean turns to persistent conversation history
    # Hash tags appended to assistant turn (~5 tokens each) — no RAG content
    conversation_history.append(user_message)
    if elected_hashes:
        assistant_text = final_response + format_hash_tags(elected_hashes)
    else:
        assistant_text = final_response
    conversation_history.append(assistant_text)

    # Step 9: Complete audit log with elected hashes (post-inference)
    database.update(message_record.id, {
        "rag_entries_elected": elected_hashes
    })

    return final_response
```

Implementation notes:

The retrieval request in Step 6 can be implemented as a tool or function call. For example, using an OpenAI-style API one could define a function retrieve_entry(id: integer) that returns the chunk text. The model would then output a structured call rather than a plaintext RETRIEVE command. Either approach works; the key is that the format is unambiguous and parseable.

The background argument in Steps 5, 7a, and 7b is a transient context channel — distinct from the persistent messages array. In most frameworks this maps to a hidden system message, a tool result, or a dedicated context slot. What matters architecturally is that content passed via background is never appended to conversation_history.

The database write in Step 3 occurs before inference. This ensures candidate IDs are recorded regardless of whether inference succeeds. Step 9 completes the two-phase audit by recording which entries were ultimately elected.

In Step 7b, the second inference call receives only the elected full_chunks as transient background — not the original summary block. The model has already evaluated the summaries in Step 5; re-including them would be redundant.

5. Comparison to Related Work

Naive RAG (Lewis et al., 2020): Injects all retrieved chunks unconditionally. Fails to distinguish relevant from irrelevant, gives the model no retrieval agency, and stores all content in persistent history. EE-RAG addresses all three failure modes simultaneously.

Self-RAG (Asai et al., 2024): Introduces adaptive retrieval by training a model with "reflection tokens" that signal when retrieval is needed and allow the model to critique its own output. This is conceptually adjacent to EE-RAG's model-elected expansion. The key difference is that Self-RAG embeds retrieval selectivity in the model through training, whereas EE-RAG achieves it through prompt structure and model agency, without additional model overhead.

Sparse RAG (Zhu et al., 2025): Reduces inference cost by having the model attend selectively to the most relevant tokens from pre-filled context, using control tokens to mask irrelevant content at the attention layer. EE-RAG takes a complementary approach: it prevents irrelevant chunks from entering context at all, rather than filtering them after injection. The two methods could be layered.

Corrective RAG (Yan et al., 2024): Adds a retrieval quality evaluator that can trigger alternative actions — such as query rewriting or web search — when initial retrieval confidence is low. This improves retrieval precision but does not change how retrieved content is injected or stored. EE-RAG is agnostic to retrieval improvement strategies; a CRAG-style evaluator could be incorporated into Step 1 without altering EE-RAG's injection or isolation layers.

Contextual Retrieval (Anthropic, 2024): At index time, prepends explanatory context to chunks to improve retrieval accuracy. This operates at index time and improves the quality of candidates fetched. EE-RAG operates at inference time and governs how any retrieved candidate is used. The two approaches are compatible and potentially synergistic: better retrieval at index time combined with EE-RAG's selective use at query time.

6. Key Properties and Benefits

Estimated token efficiency: Based on representative chunk size assumptions, summary-first retrieval is expected to reduce context injection by approximately 68–95% compared to naive RAG, depending on how many chunks are elected. Actual savings will vary by workload and chunk characteristics.

Retrieval precision: The model only receives chunks it explicitly requested. Irrelevant documents are never injected, which should raise effective answer precision.

Context window preservation: Because retrieved content is transient, context window consumption grows only with genuine conversation turns. Long conversations are expected to remain coherent; accumulated RAG snippets from prior turns never clog the persistent context.

History hygiene: Persistent conversation history contains only user and assistant text. No retrieval artifact can pollute the KB or influence future queries through re-indexing.

Pre-inference auditability: Message records are annotated with candidate IDs at write time — before inference — providing a complete picture of what the model was offered at the moment of each query, independent of inference outcome.

Two-phase retrieval audit: The combination of candidate ID annotation on the message record and hash reference persistence on the assistant turn creates a full provenance trail — what was offered, what was elected, what informed the response — at near-zero token cost.

Silent digestion: The optional silent digestion mode allows retrieved knowledge to inform responses without appearing in the inference context in any form, extending the invisible infrastructure principle to its logical limit.

Implementation agnosticism: The retrieval request mechanism — plaintext command, tool call, function call — is an implementation detail. The architectural properties hold regardless of the mechanism chosen.

Invisible infrastructure: The retrieval process leaves no trace in the persistent conversation record. The model responds as if from knowledge, producing more natural and coherent multi-turn interactions.

7. Implementation Considerations

Retrieval request format: The RETRIEVE <id> plaintext command shown above is one implementation option. In production systems, a structured function call is generally preferable for reliable parsing. For example, using an OpenAI-style API:

```json
{
  "functions": [
    {
      "name": "retrieve_entry",
      "parameters": {
        "type": "object",
        "properties": {
          "ids": { "type": "array", "items": { "type": "integer" } }
        }
      }
    }
  ]
}
```

The model would then output a structured call rather than plaintext. Either format works as long as it is unambiguous and the parser handles it consistently.

Summary generation: Summaries must be prepared at index time to avoid extra latency at query time. They can be authored manually or generated by an LLM when documents are added to the KB. Target length is approximately 20–30 tokens. Consistency in summary style — format, level of detail, vocabulary — helps the model parse and compare candidates reliably.

Candidate header consistency: The header framing the summary list should remain stable across turns. It should be injected as transient background context, not as a modification to the system prompt or a persistent user turn, so that it never enters the saved history.

Hash format: The recommended format is an 8-character hexadecimal digest of the chunk's embedding vector, prefixed with [RAG:]. Eight hex characters provides approximately 4.3 billion unique identifiers, which is sufficient for most knowledge bases. Implementations with very large indexes may extend to 12–16 characters. The tag costs approximately 4–6 tokens per elected entry.

Message record schema: The message record should include at minimum two RAG metadata fields: rag_candidates_surfaced (written pre-inference, containing candidate IDs) and rag_entries_elected (written post-inference, containing chunk hashes). These fields are metadata only — they do not enter the messages array and consume no context tokens.

Transient injection pathway: Ensure that summaries and chunks are fed to the model outside the persistent message log. Many frameworks support multiple context channels. The key invariant is that retrieved text must never land in the array that is carried forward to the next turn.

Silent digestion synthesis: In silent mode, the synthesis step should produce a compact, factual distillation of the elected chunks — sufficient to orient the model without replicating chunk content verbatim. The synthesis is used once and discarded; it should not be cached or persisted.

Edge cases: If the model requests an invalid ID, handle it gracefully. If the model requests more chunks than the token budget allows, either truncate or prompt the model to narrow the request. If retrieval fails entirely, proceed without injecting extra context. These are engineering details beyond the core architecture.

8. Future Work

EE-RAG is presented here as an architectural proposal. The token efficiency estimates in Section 3.1 and the coherence properties described throughout are derived from first-principles analysis and are consistent with findings in the literature on context length and LLM distraction (Shi et al., 2023; Liu et al., 2024). Empirical validation is the natural next step.

A recommended evaluation would compare EE-RAG against a standard naive RAG baseline — same knowledge base, same base LLM — on multi-turn QA or dialog tasks with factual answers. Useful metrics would include answer accuracy (F1 or exact match), total input tokens consumed per turn, context window utilization across turns, and coherence or hallucination rate over long conversations. Tracking how often the model elects zero chunks versus one or more would also illuminate how effectively the summary-first layer reduces unnecessary retrieval.

Two-phase audit logging is straightforward to verify empirically: candidate IDs and elected hashes can be recorded during any live run and inspected for consistency. Hash collision rates for a given KB size can be estimated analytically from the digest length.

9. Conclusion

Elective Ephemeral RAG rethinks retrieval as a cooperative, background process. The five layers of EE-RAG — summary-first retrieval with candidate header framing, model-elected expansion, transient injection with hash persistence, history isolation, and optional silent digestion — remove the noisy assumptions of naive RAG and replace them with a pipeline that treats the model as an active participant in its own context management.

By giving the model agency over what it retrieves, keeping all retrieved content transient, and recording only minimal hash references in the persistent record, EE-RAG is expected to produce systems that are more token-efficient, more precise, and more coherent over long conversations. The pre-inference write pattern and two-phase audit trail provide full retrieval provenance at a cost measured in metadata fields, not tokens.

The core insight is simple: a retrieval system should behave like a smart colleague's background knowledge, not like a filing cabinet being emptied onto the desk. You tell a smart colleague what you're trying to solve. They tell you what they know that might be relevant. You ask for details on what matters. They answer. Neither of you reads the filing cabinet out loud. And afterward, there's no pile of papers on the table — just the conversation, and the conclusion.

That is what EE-RAG does.

References

Lewis, P., Perez, E., Piktus, A., Petroni, F., Karpukhin, V., Goyal, N., Küttler, H., Lewis, M., Yih, W., Rocktäschel, T., Riedel, S., & Kiela, D. (2020). Retrieval-Augmented Generation for Knowledge-Intensive NLP Tasks. Advances in Neural Information Processing Systems 33 (NeurIPS 2020), pp. 9459–9474. https://arxiv.org/abs/2005.11401

Asai, A., Wu, Z., Wang, Y., Sil, A., & Hajishirzi, H. (2024). Self-RAG: Learning to Retrieve, Generate, and Critique through Self-Reflection. The Twelfth International Conference on Learning Representations (ICLR 2024). https://arxiv.org/abs/2310.11511

Yan, S.-Q., Gu, J.-C., Zhu, Y., & Ling, Z.-H. (2024). Corrective Retrieval Augmented Generation. arXiv preprint arXiv:2401.15884. https://arxiv.org/abs/2401.15884

Anthropic. (2024). Contextual Retrieval. Anthropic Research Blog. https://www.anthropic.com/news/contextual-retrieval

Zhu, Y., Gu, J.-C., Sikora, C., Ko, H., Liu, Y., Lin, C.-C., Shu, L., Luo, L., Meng, L., Liu, B., & Chen, J. (2025). Accelerating Inference of Retrieval-Augmented Generation via Sparse Context Selection. International Conference on Learning Representations (ICLR 2025). https://arxiv.org/abs/2405.16178

Shi, F., Chen, X., Misra, K., Scales, N., Dohan, D., Chi, E. H., Schärli, N., & Zhou, D. (2023). Large Language Models Can Be Easily Distracted by Irrelevant Context. Proceedings of the 40th International Conference on Machine Learning (ICML 2023), PMLR 202, pp. 31210–31227. https://arxiv.org/abs/2302.00093

Liu, N. F., Lin, K., Hewitt, J., Paranjape, A., Bevilacqua, M., Petroni, F., & Liang, P. (2024). Lost in the Middle: How Language Models Use Long Contexts. Transactions of the Association for Computational Linguistics, 12, pp. 157–173. https://arxiv.org/abs/2307.03172

John Brodowski, March 7, 2026 — Rev 4: March 9, 2026. Free to share with attribution.
