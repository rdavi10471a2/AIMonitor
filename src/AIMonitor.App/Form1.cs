using System.ComponentModel;
using AIMonitor.App.Controls;

namespace AIMonitor.App;

[DesignerCategory("Code")]
public sealed class Form1 : Form
{
    public Form1(AppStartupOptions options)
    {
        Text = "AIMonitor";
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1100, 720);

        Controls.Add(new MonitorDashboardControl(options)
        {
            Dock = DockStyle.Fill
        });
    }
}
