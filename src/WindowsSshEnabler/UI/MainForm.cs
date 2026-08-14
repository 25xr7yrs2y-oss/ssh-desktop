using WindowsSshEnabler.Core;

namespace WindowsSshEnabler.UI;

public sealed class MainForm : Form, IStatusSink
{
    private readonly EnablerController controller;
    private readonly Button enableButton;
    private readonly TextBox statusArea;

    public MainForm(EnablerController controller)
    {
        this.controller = controller;
        Text = "Windows SSH Enabler";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        ClientSize = new Size(620, 330);
        Font = new Font("Segoe UI", 10F);

        enableButton = new Button
        {
            Text = "Enable SSH Server",
            Location = new Point(20, 20),
            Size = new Size(580, 48),
            TabIndex = 0
        };
        enableButton.Click += EnableButton_Click;

        statusArea = new TextBox
        {
            ReadOnly = true,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Location = new Point(20, 84),
            Size = new Size(580, 226),
            TabIndex = 1,
            Text = "Ready. This utility will enable the installed Windows OpenSSH Server and add a restricted LAN firewall rule."
        };

        Controls.Add(enableButton);
        Controls.Add(statusArea);
        AcceptButton = enableButton;
    }

    public void Report(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => Report(message)));
            return;
        }
        statusArea.AppendText($"{Environment.NewLine}{DateTime.Now:T}  {message}");
    }

    private async void EnableButton_Click(object? sender, EventArgs e)
    {
        enableButton.Enabled = false;
        statusArea.Clear();
        Report("Starting safety checks...");
        try
        {
            var result = await Task.Run(() => controller.Run(this));
            Report(result.Message);
            if (!result.Success)
            {
                MessageBox.Show(this, result.Message, result.PartialSuccess ? "Partial success" : "Unable to enable SSH",
                    MessageBoxButtons.OK, result.PartialSuccess ? MessageBoxIcon.Warning : MessageBoxIcon.Error);
            }
        }
        catch (Exception ex)
        {
            var message = $"The operation could not be completed: {ex.Message}";
            Report(message);
            MessageBox.Show(this, message, "Unexpected error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { enableButton.Enabled = true; }
    }
}
