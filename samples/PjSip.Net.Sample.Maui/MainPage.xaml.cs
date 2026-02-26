using Microsoft.Extensions.DependencyInjection;
using PjSip.Net.Accounts;
using PjSip.Net.Calls;
using PjSip.Net.DependencyInjection;
using PjSip.Net.Media;
using PjSip.Net.Transport;

namespace PjSip.Net.Sample.Maui;

public partial class MainPage : ContentPage
{
    private ISipPhone? _phone;
    private ISipCall? _activeCall;

    public MainPage()
    {
        InitializeComponent();
    }

    private async void OnStartClicked(object? sender, EventArgs e)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPjSip(options =>
        {
            options.Transports.Add(new SipTransportOptions
            {
                Type = SipTransportType.Udp
            });
            options.Accounts.Add(new SipAccountOptions
            {
                Username = "alice",
                Password = "secret",
                Domain = "sip.example.com"
            });
        });

        var provider = services.BuildServiceProvider();
        _phone = provider.GetRequiredService<ISipPhone>();
        // Audio route change handling (Bluetooth, headset, CarPlay, Android Auto)
        _phone.Audio.AudioRouteChanged += (s, args) =>
            MainThread.BeginInvokeOnMainThread(() =>
            {
                StatusLabel.Text = $"Audio: {args.Reason} → {args.NewDeviceName}";
                if (args.NewDeviceName is not null)
                    _phone.Audio.SetOutputDeviceByName(args.NewDeviceName);
            });

        await _phone.StartAsync();
        StatusLabel.Text = $"Status: {_phone.State}";
    }

    private void OnCallClicked(object? sender, EventArgs e)
    {
        if (_phone is { State: SipPhoneState.Running, Accounts.Count: > 0 })
        {
            var destination = string.IsNullOrWhiteSpace(DestinationEntry.Text)
                ? "sip:bob@example.com"
                : DestinationEntry.Text;
            _activeCall = _phone.MakeCall(_phone.Accounts[0], destination);
            StatusLabel.Text = $"Calling {destination}...";
        }
    }

    private void OnHangupClicked(object? sender, EventArgs e)
    {
        _activeCall?.Hangup();
        _activeCall = null;
        StatusLabel.Text = "Status: Idle";
    }

    private async void OnStopClicked(object? sender, EventArgs e)
    {
        if (_phone is not null)
        {
            await _phone.StopAsync();
            StatusLabel.Text = "Status: Stopped";
        }
    }
}
