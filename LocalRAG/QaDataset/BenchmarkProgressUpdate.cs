namespace LocalRAG.QaDataset
{
    /// <summary>
    /// Structured progress update emitted after each question completes during a benchmark run.
    /// </summary>
    public class BenchmarkProgressUpdate
    {
        /// <summary>Human-readable status line (question preview or status message).</summary>
        public string Message { get; set; } = "";

        /// <summary>Number of questions completed so far (0 on the initial start message).</summary>
        public int Done { get; set; }

        /// <summary>Total number of questions in this run.</summary>
        public int Total { get; set; }

        // Running outcome counts (updated after every question)
        public int Correct { get; set; }
        public int PossiblyCorrect { get; set; }
        public int DefinitelyWrong { get; set; }
        public int Indeterminate { get; set; }

        // Computed rates (0–100)
        public double CorrectRate        => Done == 0 ? 0 : (double)Correct        / Done * 100;
        public double PossiblyCorrectRate => Done == 0 ? 0 : (double)PossiblyCorrect / Done * 100;
        public double DefinitelyWrongRate => Done == 0 ? 0 : (double)DefinitelyWrong / Done * 100;
        public double IndeterminateRate   => Done == 0 ? 0 : (double)Indeterminate   / Done * 100;
    }
}
