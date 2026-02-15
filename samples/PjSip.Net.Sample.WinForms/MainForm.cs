using Microsoft.Extensions.DependencyInjection;
using PjSip.Net.Accounts;
using PjSip.Net.DependencyInjection;
using PjSip.Net.Transport;

namespace PjSip.Net.Sample.WinForms;

public partial class MainForm : Form
{
    private ISipPhone? _phone;

    public MainForm()
    {
        Text = "PjSip.Net WinForms Sample";
        Size = new System.Drawing.Size(400, 300);
        StartPosition = FormStartPosition.CenterScreen;

        var btnStart = new Button { Text = "Start", Dock = DockStyle.Top };
        btnStart.Click += async (s, e) => await StartPhoneAsync();

        var btnCall = new Button { Text = "Make Call", Dock = DockStyle.Top };
        btnCall.Click += (s, e) => MakeCall();

        var btnStop = new Button { Text = "Stop", Dock = DockStyle.Top };
        btnStop.Click += async (s, e) => await StopPhoneAsync();

        Controls.AddRange([btnStop, btnCall, btnStart]);
    }

    private async Task StartPhoneAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPjSip(options =>
        {
            options.Transports.Add(new SipTransportOptions { Type = SipTransportType.Udp });
            options.Accounts.Add(new SipAccountOptions
            {
                Username = "alice",
                Password = "secret",
                Domain = "sip.example.com"
            });
        });

        var provider = services.BuildServiceProvider();
        _phone = provider.GetRequiredService<ISipPhone>();

        _phone.IncomingCall += (s, e) =>
            BeginInvoke(() => MessageBox.Show($"Incoming call from {e.RemoteUri}"));

        await _phone.StartAsync();
        Text = $"PjSip.Net WinForms — {_phone.State}";
    }

    private void MakeCall()
    {
        if (_phone is { State: SipPhoneState.Running, Accounts.Count: > 0 })
        {
            _phone.MakeCall(_phone.Accounts[0], "sip:bob@example.com");
        }
    }

    private async Task StopPhoneAsync()
    {
        if (_phone is not null)
        {
            await _phone.StopAsync();
            Text = "PjSip.Net WinForms — Stopped";
        }
    }
}
