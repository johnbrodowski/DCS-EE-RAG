namespace DemoApp
{
    /// <summary>
    /// Double-buffered panel that renders a real-time line chart of benchmark metrics.
    /// Supports two modes: QA (Correct/PossCorrect/Wrong/Indeterminate rates) and
    /// DCS (F1/Precision/Recall/CandidateRecall).
    /// </summary>
    public sealed class BenchmarkChart : Panel
    {
        // ── Series data ──────────────────────────────────────────────────────

        public enum ChartMode { Qa, Dcs }

        private ChartMode _mode = ChartMode.Qa;
        private readonly List<double[]> _dataPoints = [];
        private string[] _seriesNames = QaSeriesNames;
        private Color[] _seriesColors = QaSeriesColors;

        private static readonly string[] QaSeriesNames =
            ["Correct %", "Poss. Correct %", "Def. Wrong %", "Indeterminate %"];
        private static readonly Color[] QaSeriesColors =
            [Color.FromArgb(80, 220, 100), Color.FromArgb(255, 200, 60), Color.FromArgb(240, 70, 70), Color.FromArgb(140, 140, 140)];

        private static readonly string[] DcsSeriesNames =
            ["F1", "Precision", "Recall", "Cand. Recall"];
        private static readonly Color[] DcsSeriesColors =
            [Color.FromArgb(80, 180, 255), Color.FromArgb(80, 220, 100), Color.FromArgb(255, 160, 50), Color.FromArgb(200, 120, 255)];

        // ── Rendering constants ──────────────────────────────────────────────

        private const int MarginLeft = 44;
        private const int MarginRight = 12;
        private const int MarginTop = 30;
        private const int MarginBottom = 28;
        private const int LegendHeight = 20;

        private static readonly Color BackColor_ = Color.FromArgb(30, 30, 30);
        private static readonly Color GridColor = Color.FromArgb(55, 55, 55);
        private static readonly Color AxisColor = Color.FromArgb(100, 100, 100);
        private static readonly Color LabelColor = Color.FromArgb(170, 170, 170);
        private static readonly Color TitleColor = Color.FromArgb(200, 200, 200);

        // ── Constructor ──────────────────────────────────────────────────────

        public BenchmarkChart()
        {
            DoubleBuffered = true;
            BackColor = BackColor_;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
        }

        // ── Public API ───────────────────────────────────────────────────────

        public void SetMode(ChartMode mode)
        {
            _mode = mode;
            _seriesNames = mode == ChartMode.Dcs ? DcsSeriesNames : QaSeriesNames;
            _seriesColors = mode == ChartMode.Dcs ? DcsSeriesColors : QaSeriesColors;
            Clear();
        }

        /// <summary>
        /// Add a data point. Values are percentages (0-100) for each series.
        /// QA mode expects 4 values: [correct%, possCorrect%, defWrong%, indeterminate%].
        /// DCS mode expects 4 values: [f1%, precision%, recall%, candidateRecall%].
        /// </summary>
        public void AddPoint(double[] values)
        {
            if (values.Length < _seriesNames.Length) return;
            _dataPoints.Add((double[])values.Clone());
            Invalidate();
        }

        public void Clear()
        {
            _dataPoints.Clear();
            Invalidate();
        }

        // ── Painting ─────────────────────────────────────────────────────────

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int w = Width, h = Height;
            if (w < 80 || h < 80) return; // too small to render

            var plotLeft = MarginLeft;
            var plotRight = w - MarginRight;
            var plotTop = MarginTop;
            var plotBottom = h - MarginBottom - LegendHeight;
            var plotW = plotRight - plotLeft;
            var plotH = plotBottom - plotTop;

            if (plotW < 20 || plotH < 20) return;

            using var fontSmall = new Font("Consolas", 7.5f);
            using var fontTitle = new Font("Segoe UI", 9f, FontStyle.Bold);
            using var penGrid = new Pen(GridColor, 1f);
            using var penAxis = new Pen(AxisColor, 1f);
            using var brushLabel = new SolidBrush(LabelColor);
            using var brushTitle = new SolidBrush(TitleColor);

            // ── Title ────────────────────────────────────────────────────────
            var title = _mode == ChartMode.Dcs ? "DCS / EE-RAG Metrics" : "QA Benchmark Metrics";
            g.DrawString(title, fontTitle, brushTitle, plotLeft, 6);

            // ── Y-axis grid (0%, 25%, 50%, 75%, 100%) ────────────────────
            for (int pct = 0; pct <= 100; pct += 25)
            {
                float y = plotBottom - (pct / 100f * plotH);
                g.DrawLine(penGrid, plotLeft, y, plotRight, y);

                var label = $"{pct}%";
                var sz = g.MeasureString(label, fontSmall);
                g.DrawString(label, fontSmall, brushLabel, plotLeft - sz.Width - 4, y - sz.Height / 2);
            }

            // ── Axes ─────────────────────────────────────────────────────────
            g.DrawLine(penAxis, plotLeft, plotTop, plotLeft, plotBottom);
            g.DrawLine(penAxis, plotLeft, plotBottom, plotRight, plotBottom);

            // ── Data lines ───────────────────────────────────────────────────
            int count = _dataPoints.Count;
            if (count < 1)
            {
                // Empty state message
                var msg = "Waiting for benchmark data...";
                var msgSz = g.MeasureString(msg, fontSmall);
                g.DrawString(msg, fontSmall, brushLabel,
                    plotLeft + (plotW - msgSz.Width) / 2,
                    plotTop + (plotH - msgSz.Height) / 2);
            }
            else
            {
                int seriesCount = _seriesNames.Length;
                float xStep = count > 1 ? (float)plotW / (count - 1) : 0;

                for (int s = 0; s < seriesCount; s++)
                {
                    using var pen = new Pen(_seriesColors[s], 2f);

                    if (count == 1)
                    {
                        // Single point — draw a dot
                        float x = plotLeft + plotW / 2f;
                        float y = plotBottom - ((float)_dataPoints[0][s] / 100f * plotH);
                        g.FillEllipse(new SolidBrush(_seriesColors[s]), x - 3, y - 3, 6, 6);
                    }
                    else
                    {
                        var points = new PointF[count];
                        for (int i = 0; i < count; i++)
                        {
                            float x = plotLeft + i * xStep;
                            float val = (float)Math.Clamp(_dataPoints[i][s], 0, 100);
                            float y = plotBottom - (val / 100f * plotH);
                            points[i] = new PointF(x, y);
                        }
                        g.DrawLines(pen, points);

                        // Draw dot on the last point
                        var last = points[^1];
                        g.FillEllipse(new SolidBrush(_seriesColors[s]),
                            last.X - 3, last.Y - 3, 6, 6);
                    }
                }

                // ── X-axis labels (first, middle, last) ──────────────────────
                if (count >= 1)
                {
                    void DrawXLabel(int idx)
                    {
                        float x = count == 1
                            ? plotLeft + plotW / 2f
                            : plotLeft + idx * xStep;
                        var label = (idx + 1).ToString();
                        var sz = g.MeasureString(label, fontSmall);
                        g.DrawString(label, fontSmall, brushLabel, x - sz.Width / 2, plotBottom + 4);
                    }

                    DrawXLabel(0);
                    if (count > 2) DrawXLabel(count / 2);
                    if (count > 1) DrawXLabel(count - 1);
                }
            }

            // ── Legend ───────────────────────────────────────────────────────
            float legendY = h - LegendHeight + 2;
            float legendX = plotLeft;
            int seriesN = _seriesNames.Length;

            for (int s = 0; s < seriesN; s++)
            {
                using var brush = new SolidBrush(_seriesColors[s]);
                g.FillRectangle(brush, legendX, legendY + 2, 10, 10);

                var text = _seriesNames[s];
                if (count > 0)
                {
                    var lastVal = _dataPoints[^1][s];
                    text += $" {lastVal:F1}%";
                }

                var tsz = g.MeasureString(text, fontSmall);
                g.DrawString(text, fontSmall, brushLabel, legendX + 13, legendY);
                legendX += 13 + tsz.Width + 8;
            }
        }
    }
}
