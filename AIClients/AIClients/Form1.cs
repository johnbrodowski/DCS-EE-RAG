using AiMessagingCore.Abstractions;
using AiMessagingCore.Configuration;
using AiMessagingCore.Core;
using AiMessagingCore.Providers.Anthropic;
using AiMessagingCore.Providers.DeepSeek;
using AiMessagingCore.Providers.Duck;
using AiMessagingCore.Providers.Grok;
using AiMessagingCore.Providers.Groq;
using AiMessagingCore.Providers.Local;
using AiMessagingCore.Providers.OpenAI;

namespace AIClients
{
    public partial class Form1 : Form
    {
        private IAiProviderFactory? _factory;
        private IChatSession? _session;
        private CancellationTokenSource? _cts;
        private AiLibrarySettings _settings = new();

        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            var localModels = new InMemoryLocalModelManager();
            IAiProvider[] providers =
            [
                new OpenAiProvider(),
                new AnthropicProvider(),
                new DeepSeekProvider(),
                new GrokProvider(),
                new GroqProvider(),
                new DuckProvider(),
                new LmStudioProvider(localModels),
                new LlamaSharpProvider(localModels)
            ];

            _factory = new AiProviderFactory(providers);
            cboProvider.Items.AddRange(_factory.RegisteredProviders.OrderBy(x => x).Cast<object>().ToArray());

            _settings = AiSettings.Load();
            AiSettings.ApplyToEnvironment(_settings);

            cboProvider.SelectedIndexChanged += CboProvider_SelectedIndexChanged;

            var defaultProvider = _settings.DefaultProvider;
            cboProvider.SelectedItem = _factory.RegisteredProviders
                .FirstOrDefault(p => p.Equals(defaultProvider, StringComparison.OrdinalIgnoreCase))
                ?? "OpenAI";

            if (cboProvider.SelectedItem?.ToString() is string sel
                && _settings.Providers.TryGetValue(sel, out var ps))
            {
                LoadProviderFields(ps.Defaults);
            }

            btnCancel.Enabled = false;

            AppendOutput($"Settings loaded from: {AiSettings.DefaultFilePath}\n");
        }

        private void CboProvider_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (cboProvider.SelectedItem?.ToString() is not string provider) return;
            if (_settings.Providers.TryGetValue(provider, out var ps))
                LoadProviderFields(ps.Defaults);
        }

        private void LoadProviderFields(ModelDefaults defaults)
        {
            txtModel.Text         = defaults.Model;
            txtSystemMessage.Text = defaults.SystemPrompt;
            txtPrompt.Text        = defaults.SampleQuery;
        }

        private void btnCreateSession_Click(object sender, EventArgs e)
        {
            if (_factory is null || cboProvider.SelectedItem is null || string.IsNullOrWhiteSpace(txtModel.Text))
                return;

            if (cboProvider.SelectedItem.ToString() is string provider
                && _settings.Providers.TryGetValue(provider, out var ps))
            {
                ps.Defaults.Model        = txtModel.Text.Trim();
                ps.Defaults.SystemPrompt = txtSystemMessage.Text.Trim();
                ps.Defaults.SampleQuery  = txtPrompt.Text.Trim();
                AiSettings.Save(_settings);
            }

            _cts?.Cancel();

            var builder = new AiSessionBuilder(_factory, cboProvider.SelectedItem.ToString()!)
                .WithModel(txtModel.Text.Trim())
                .WithStreaming();

            var systemMessage = txtSystemMessage.Text.Trim();
            if (!string.IsNullOrWhiteSpace(systemMessage))
                builder = builder.WithSystemMessage(systemMessage);

            _session = builder.Build();

            _session.OnResponseStarted   += (_, _)   => AppendOutput($"\n[{DateTime.Now:T}] Response started\n");
            _session.OnTokenReceived     += (_, e2)   => AppendOutput(e2.Token);
            _session.OnCancelled         += (_, _)    => AppendOutput($"\n[{DateTime.Now:T}] Cancelled\n");
            _session.OnError             += (_, e2)   => AppendOutput($"\n[{DateTime.Now:T}] Error: {e2.Message}\n");
            _session.OnResponseCompleted += (_, e2)   =>
                AppendOutput($"\n\n[{e2.ProviderName}/{e2.ModelName}] total={e2.TotalTokens} tps={e2.TokensPerSecond:F2} ttfb={e2.TimeToFirstToken.TotalMilliseconds:F0}ms\n");

            AppendOutput($"Session created: {cboProvider.SelectedItem} / {txtModel.Text.Trim()}");
            if (!string.IsNullOrWhiteSpace(systemMessage))
                AppendOutput(" (system prompt set)");
            AppendOutput("\n");
        }

        private async void btnSend_Click(object sender, EventArgs e)
        {
            if (_session is null || string.IsNullOrWhiteSpace(txtPrompt.Text))
                return;

            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            btnCancel.Enabled = true;
            btnSend.Enabled   = false;

            try
            {
                await _session.SendAsync(txtPrompt.Text.Trim(), cancellationToken: _cts.Token);
            }
            catch (OperationCanceledException)
            {
                AppendOutput($"[{DateTime.Now:T}] Request cancelled by user.\n");
            }
            finally
            {
                btnCancel.Enabled = false;
                btnSend.Enabled   = true;
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            _cts?.Cancel();
        }

        private void AppendOutput(string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => AppendOutput(text));
                return;
            }

            rtbOutput.AppendText(text);
        }
    }
}
