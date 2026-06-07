using System.ComponentModel;
using System.Text.Json;
using AIMonitor.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace AIMonitor.App.Controls
{
    [DesignerCategory("Code")]
    internal sealed class MarkdownPreviewControl : UserControl
    {
        private const string MarkdownHostName = "aimonitor-markdown.local";

        private readonly WebView2 webView;
        private readonly TextBox diagnosticTextBox;

        private string pendingMarkdown = string.Empty;
        private bool controlReady;
        private bool webViewInitialized;
        private bool webViewInitializationStarted;
        private TaskCompletionSource<bool>? webViewReadyCompletion;

        public MarkdownPreviewControl()
        {
            Dock = DockStyle.Fill;
            webView = new WebView2
            {
                Dock = DockStyle.Fill,
                DefaultBackgroundColor = Color.White
            };
            diagnosticTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = new Font("Consolas", 9),
                Visible = false
            };

            Controls.Add(diagnosticTextBox);
            Controls.Add(webView);
            Load += (_, _) =>
            {
                BeginInvoke((MethodInvoker)(() =>
                {
                    controlReady = true;
                    _ = RenderPendingMarkdownAsync();
                }));
            };
        }

        public void SetMarkdown(string markdown)
        {
            pendingMarkdown = markdown;
            diagnosticTextBox.Visible = false;
            webView.Visible = true;
            if (!controlReady)
            {
                return;
            }

            _ = RenderPendingMarkdownAsync();
        }

        public void ShowDiagnostic(string text)
        {
            webView.Visible = false;
            diagnosticTextBox.Text = text;
            diagnosticTextBox.Visible = true;
        }

        private async Task RenderPendingMarkdownAsync()
        {
            try
            {
                await EnsureWebViewAsync();
                if (webView.CoreWebView2 is null || !webViewInitialized)
                {
                    return;
                }

                string payload = JsonSerializer.Serialize(pendingMarkdown).Replace("</", "<\\/", StringComparison.Ordinal);
                await webView.CoreWebView2.ExecuteScriptAsync($"window.aimonitorSetMarkdown({payload});");
            }
            catch (Exception ex)
            {
                ShowDiagnostic("Markdown preview failed to render." + Environment.NewLine + Environment.NewLine + ex);
            }
        }

        private async Task EnsureWebViewAsync()
        {
            if (webViewInitialized)
            {
                return;
            }

            if (webViewInitializationStarted)
            {
                if (webViewReadyCompletion is not null)
                {
                    await webViewReadyCompletion.Task.WaitAsync(TimeSpan.FromSeconds(15));
                }

                return;
            }

            webViewInitializationStarted = true;
            webViewReadyCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            CoreWebView2Environment environment = await CreateWebViewEnvironmentAsync();
            await webView.EnsureCoreWebView2Async(environment);
            if (webView.CoreWebView2 is not null)
            {
                string markdownAssetsFolder = Path.Combine(AppContext.BaseDirectory, "Assets", "markdown");
                if (Directory.Exists(markdownAssetsFolder))
                {
                    webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                        MarkdownHostName,
                        markdownAssetsFolder,
                        CoreWebView2HostResourceAccessKind.Allow);
                }

                webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                webView.CoreWebView2.NavigationCompleted += (_, args) =>
                {
                    if (!args.IsSuccess)
                    {
                        webViewReadyCompletion?.TrySetException(
                            new InvalidOperationException($"Markdown preview navigation failed: {args.WebErrorStatus}"));
                    }
                };
                webView.CoreWebView2.ProcessFailed += (_, args) =>
                {
                    webViewReadyCompletion?.TrySetException(
                        new InvalidOperationException($"Markdown preview WebView2 process failed: {args.ProcessFailedKind}"));
                };
                webView.CoreWebView2.WebMessageReceived += (_, args) =>
                {
                    if (args.WebMessageAsJson.Contains("\"ready\"", StringComparison.OrdinalIgnoreCase))
                    {
                        webViewInitialized = true;
                        webViewReadyCompletion?.TrySetResult(true);
                    }

                    if (args.WebMessageAsJson.Contains("\"error\"", StringComparison.OrdinalIgnoreCase))
                    {
                        ShowDiagnostic($"Markdown preview error: {args.WebMessageAsJson}");
                    }
                };
            }

            webView.NavigateToString(RenderPreviewShellHtml());
            await webViewReadyCompletion.Task.WaitAsync(TimeSpan.FromSeconds(15));
            webViewInitializationStarted = false;
        }

        private static async Task<CoreWebView2Environment> CreateWebViewEnvironmentAsync()
        {
            string repositoryRoot = AppPathResolver.FindRepositoryRoot();
            string userDataFolder = Path.Combine(repositoryRoot, "runtime", "self-analysis-codex", "webview2-user-data");
            Directory.CreateDirectory(userDataFolder);
            return await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
        }

        private static string RenderPreviewShellHtml()
        {
            return """
<!doctype html>
<html>
<head>
  <meta charset="utf-8">
  <script src="https://aimonitor-markdown.local/markdown-it.min.js"></script>
  <style>
    :root {
      color-scheme: light;
      font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Helvetica, Arial, sans-serif;
      color: #24292f;
      background: #f6f8fa;
    }

    body {
      margin: 0;
      padding: 0;
      font-size: 16px;
      line-height: 1.5;
    }

    .markdown-body {
      max-width: 960px;
      min-height: calc(100vh - 56px);
      margin: 0 auto 32px;
      padding: 34px 48px 64px;
      background: #ffffff;
      border-left: 1px solid #d8dee4;
      border-right: 1px solid #d8dee4;
      box-shadow: 0 8px 28px rgba(31, 35, 40, 0.07);
    }

    h1, h2, h3, h4, h5, h6 {
      margin-top: 24px;
      margin-bottom: 16px;
      font-weight: 600;
      line-height: 1.25;
    }

    h1 {
      margin-top: 0;
      padding-bottom: 0.3em;
      font-size: 1.9em;
      border-bottom: 1px solid #d8dee4;
      color: #111827;
    }

    h2 {
      margin-top: 30px;
      padding-bottom: 0.3em;
      font-size: 1.35em;
      border-bottom: 1px solid #d8dee4;
      color: #1f2937;
    }

    h3 {
      font-size: 1.12em;
      margin-top: 18px;
      margin-bottom: 6px;
    }

    p, blockquote, ul, ol, dl, table, pre {
      margin-top: 0;
      margin-bottom: 16px;
    }

    p {
      max-width: 78ch;
    }

    h3 + p {
      margin-bottom: 12px;
    }

    ul, ol {
      padding-left: 2em;
    }

    li {
      margin: 0.25em 0;
    }

    code {
      padding: 0.2em 0.4em;
      font-size: 85%;
      font-family: ui-monospace, SFMono-Regular, "SF Mono", Consolas, "Liberation Mono", Menlo, monospace;
      background-color: rgba(175, 184, 193, 0.2);
      border-radius: 6px;
    }

    pre {
      padding: 16px;
      overflow: auto;
      font-size: 85%;
      line-height: 1.45;
      background-color: #f6f8fa;
      border-radius: 6px;
    }

    pre code {
      padding: 0;
      font-size: 100%;
      background: transparent;
    }

    blockquote {
      padding: 0 1em;
      color: #57606a;
      border-left: 0.25em solid #d0d7de;
    }

    table {
      border-collapse: collapse;
      display: block;
      width: max-content;
      max-width: 100%;
      overflow: auto;
      font-size: 0.94em;
    }

    th, td {
      padding: 7px 12px;
      border: 1px solid #d0d7de;
    }

    tr {
      background-color: #ffffff;
      border-top: 1px solid #d8dee4;
    }

    tr:nth-child(2n) {
      background-color: #f6f8fa;
    }

    h1 + table {
      display: table;
      width: auto;
      min-width: 420px;
      margin-bottom: 24px;
      color: #475467;
      background: #f8fafc;
      border: 1px solid #d0d7de;
      border-radius: 6px;
      overflow: hidden;
    }

    h1 + table th {
      background: #eef2f6;
      color: #344054;
    }

    a {
      color: #0969da;
      text-decoration: none;
    }

    a:hover {
      text-decoration: underline;
    }
  </style>
</head>
<body>
  <main id="preview" class="markdown-body"></main>
  <script>
    const renderer = window.markdownit({
      html: false,
      linkify: true,
      typographer: true,
      breaks: true
    });

    function post(kind, details) {
      if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage({ kind, details: details || '' });
      }
    }

    window.aimonitorSetMarkdown = function(markdown) {
      try {
        const cleaned = String(markdown || '')
          .replace(/^\s*<!--\s*(HUMAN|SYSTEM|TASK-GIT|AI):(?:BEGIN|END)[^>]*-->\s*$/gm, '')
          .replace(/\n{3,}/g, '\n\n');
        document.getElementById('preview').innerHTML = renderer.render(cleaned);
      } catch (error) {
        post('error', error && error.message ? error.message : String(error));
      }
    };

    post('ready');
  </script>
</body>
</html>
""";
        }
    }
}
