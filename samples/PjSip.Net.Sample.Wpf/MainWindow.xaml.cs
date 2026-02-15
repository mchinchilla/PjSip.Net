using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PjSip.Net.Accounts;
using PjSip.Net.Calls;
using PjSip.Net.DependencyInjection;
using PjSip.Net.Transport;

namespace PjSip.Net.Sample.Wpf;

public partial class MainWindow : Window
{
    private ISipPhone? _phone;
    private ISipCall? _activeCall;

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void BtnStart_Click(object sender, RoutedEventArgs e)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPjSip(options =>
        {
            options.Transports.Add(new SipTransportOptions
            {
                Type = SipTransportType.Tls,
                Port = 5061
            });
            options.Accounts.Add(new SipAccountOptions
            {
                Username = "alice",
                Password = "secret",
                Domain = "sip.example.com",
                Registrar = "sip:sip.example.com"
            });
        });

        var provider = services.BuildServiceProvider();
        _phone = provider.GetRequiredService<ISipPhone>();

        _phone.IncomingCall += (s, args) =>
            Dispatcher.Invoke(() => Log($"Incoming call from {args.RemoteUri}"));

        _phone.CallStateChanged += (s, args) =>
            Dispatcher.Invoke(() => Log($"Call {args.Call.Id}: {args.OldState} -> {args.NewState}"));

        _phone.RegistrationStateChanged += (s, args) =>
            Dispatcher.Invoke(() => Log($"Account {args.Account.Uri}: {args.NewState}"));

        await _phone.StartAsync();
        StatusText.Text = $"Status: {_phone.State}";
        Log("Phone started");
    }

    private void BtnCall_Click(object sender, RoutedEventArgs e)
    {
        if (_phone is { State: SipPhoneState.Running, Accounts.Count: > 0 })
        {
            _activeCall = _phone.MakeCall(_phone.Accounts[0], TxtDestination.Text);
            Log($"Calling {TxtDestination.Text}...");
        }
    }

    private void BtnHangup_Click(object sender, RoutedEventArgs e)
    {
        _activeCall?.Hangup();
        _activeCall = null;
    }

    private async void BtnStop_Click(object sender, RoutedEventArgs e)
    {
        if (_phone is not null)
        {
            await _phone.StopAsync();
            StatusText.Text = "Status: Stopped";
            Log("Phone stopped");
        }
    }

    private void Log(string message)
    {
        LogList.Items.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        LogList.ScrollIntoView(LogList.Items[^1]);
    }
}
