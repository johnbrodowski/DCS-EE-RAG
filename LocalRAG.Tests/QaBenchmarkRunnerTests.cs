using LocalRAG.QaDataset;
using Xunit;

namespace LocalRAG.Tests;

/// <summary>
/// A failed model call must not be scored as an unanswered question (G4-4), and a short expected answer must
/// match as a word, not inside another word.
/// </summary>
public class QaBenchmarkRunnerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qa-runner-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* sqlite may still hold the file */ }
    }

    private async Task<QaDatasetDatabase> DbWithAsync(int questions)
    {
        var db = new QaDatasetDatabase(Path.Combine(_dir, "qa.db"));
        await db.InitializeAsync();
        for (int i = 0; i < questions; i++)
            await db.ImportItemAsync(new QaDatasetItem
            {
                Question = $"Question {i}?",
                AnswerAndDefCorrectPredictions = { "paris" },
            });
        return db;
    }

    private static QaBenchmarkOptions Options(int questions, int maxConsecutiveErrors) => new()
    {
        MaxQuestions = questions,
        UseRagContext = false,
        MaxConsecutiveErrors = maxConsecutiveErrors,
    };

    [Theory]
    [InlineData("a", "bananas and bread", QaOutcome.Indeterminate)]
    [InlineData("a", "It is a cat", QaOutcome.Correct)]
    [InlineData("no", "I know that", QaOutcome.Indeterminate)]
    [InlineData("No", "No, it is not", QaOutcome.Correct)]
    [InlineData("1", "In 2019 it began", QaOutcome.Indeterminate)]
    [InlineData("New York", "the answer is NEW YORK city", QaOutcome.Correct)]
    [InlineData("c++", "Use C++ here", QaOutcome.Correct)]
    public void ShortAnswersMatchAsWholeWords(string expected, string response, QaOutcome outcome)
    {
        var item = new QaDatasetItem { AnswerAndDefCorrectPredictions = { expected } };
        Assert.Equal(outcome, QaBenchmarkRunner.EvaluateAnswer(response, item).Outcome);
    }

    [Fact]
    public async Task RunStopsAfterConsecutiveModelFailuresAndIsNotValid()
    {
        using var db = await DbWithAsync(10);
        var calls = 0;
        var report = await QaBenchmarkRunner.RunAsync(Options(10, 3), db,
            _ => { calls++; throw new InvalidOperationException("401 invalid api key"); });

        Assert.Equal(3, calls);
        Assert.Equal(3, report.TotalRun);
        Assert.Equal(3, report.Errors);
        Assert.Equal(0, report.Indeterminate);
        Assert.NotNull(report.AbortReason);
        Assert.False(report.Valid);
        Assert.All(report.Results, r => Assert.Equal(QaOutcome.Error, r.Outcome));
    }

    [Fact]
    public async Task AFailedRunIsNeverJudgedAgainstThresholds()
    {
        using var db = await DbWithAsync(4);
        var report = await QaBenchmarkRunner.RunAsync(Options(4, 0), db, _ => throw new TimeoutException("rate limited"));
        report.CorrectThreshold = 0;
        report.IndeterminateThreshold = 100;

        var summary = report.FormatSummary();
        Assert.Equal(4, report.Errors);
        Assert.Contains("RESULT NOT VALID", summary);
        Assert.DoesNotContain("PASS", summary);
        Assert.DoesNotContain("FAIL", summary);
    }

    [Fact]
    public async Task ScatteredFailuresDoNotStopTheRunAndAreLeftOutOfTheRates()
    {
        using var db = await DbWithAsync(6);
        var n = 0;
        var report = await QaBenchmarkRunner.RunAsync(Options(6, 2), db,
            _ => ++n % 2 == 0 ? throw new IOException("connection reset") : Task.FromResult("Paris"));

        Assert.Equal(6, report.TotalRun);
        Assert.Equal(3, report.Errors);
        Assert.Equal(3, report.Answered);
        Assert.Equal(3, report.Correct);
        Assert.Equal(100, report.CorrectRate, 6);
        Assert.Null(report.AbortReason);
        Assert.False(report.Valid);
    }

    [Fact]
    public async Task CleanRunIsValidAndJudgedAgainstThresholds()
    {
        using var db = await DbWithAsync(3);
        var report = await QaBenchmarkRunner.RunAsync(Options(3, 5), db, _ => Task.FromResult("Paris"));
        report.CorrectThreshold = 90;

        Assert.True(report.Valid);
        Assert.Contains("PASS", report.FormatSummary());
    }
}
