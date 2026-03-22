namespace LocalRAG.QaDataset
{
    /// <summary>
    /// Backfills question embeddings for QA dataset items that have not yet been embedded.
    /// Supports start/stop so large datasets can be processed incrementally across sessions.
    /// Progress persists automatically — on restart it continues from where it left off.
    /// </summary>
    public class QaEmbeddingBackfiller
    {
        private CancellationTokenSource? _cts;
        private Task? _task;

        public bool IsRunning => _task != null && !_task.IsCompleted;

        /// <summary>Starts the backfill in the background.</summary>
        /// <param name="overwriteExisting">When true, clears all existing embeddings first so they are regenerated.</param>
        public void Start(
            QaDatasetDatabase db,
            EmbedderClassNew embedder,
            IProgress<(int Done, int Total)>? progress = null,
            Action<string>? onError = null,
            bool overwriteExisting = false)
        {
            if (IsRunning) return;

            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            _task = Task.Run(async () =>
            {
                try
                {
                    if (overwriteExisting)
                        await db.ResetAllEmbeddingsAsync();

                    await RunAsync(db, embedder, progress, ct);
                }
                catch (OperationCanceledException) { /* expected on Stop() */ }
                catch (Exception ex) { onError?.Invoke(ex.Message); }
            }, CancellationToken.None);
        }

        /// <summary>Signals the backfill to stop after the current item completes.</summary>
        public void Stop() => _cts?.Cancel();

        /// <summary>Waits for the background task to finish (useful on form close).</summary>
        public Task WaitAsync() => _task ?? Task.CompletedTask;

        private static async Task RunAsync(
            QaDatasetDatabase db,
            EmbedderClassNew embedder,
            IProgress<(int Done, int Total)>? progress,
            CancellationToken ct)
        {
            var (total, embedded) = await db.GetItemCountAsync();
            int done = embedded;
            progress?.Report((done, total));

            while (!ct.IsCancellationRequested)
            {
                var batch = await db.GetUnembeddedItemsAsync(50);
                if (batch.Count == 0) break;  // all done

                foreach (var item in batch)
                {
                    ct.ThrowIfCancellationRequested();

                    var embedding = await embedder.TryGetEmbeddingsAsync(item.Question);
                    if (embedding != null)
                    {
                        await db.UpdateEmbeddingAsync(item.Id, embedding);
                        done++;
                        progress?.Report((done, total));
                    }
                }
            }
        }
    }
}
