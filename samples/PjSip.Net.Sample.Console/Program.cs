using Microsoft.Extensions.DependencyInjection;
using PjSip.Net;
using PjSip.Net.Accounts;
using PjSip.Net.DependencyInjection;
using PjSip.Net.Media;
using PjSip.Net.Transport;

// Configure services
var services = new ServiceCollection();
services.AddLogging();
services.AddPjSip(options =>
{
    options.UserAgent = "PjSip.Net.Sample/1.0";
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
var phone = provider.GetRequiredService<ISipPhone>();

// Subscribe to events
phone.IncomingCall += (s, e) =>
{
    Console.WriteLine($"Incoming call from {e.RemoteUri}");
    e.Call.Answer();
};

phone.CallStateChanged += (s, e) =>
{
    Console.WriteLine($"Call {e.Call.Id}: {e.OldState} -> {e.NewState}");
};

phone.RegistrationStateChanged += (s, e) =>
{
    Console.WriteLine($"Account {e.Account.Uri}: {e.OldState} -> {e.NewState}");
};

// Start the phone
Console.WriteLine("Starting SIP phone...");
await phone.StartAsync();
Console.WriteLine($"Phone started. State: {phone.State}");
Console.WriteLine($"Registered accounts: {phone.Accounts.Count}");

// Audio device management
var audio = phone.Audio;

// List available devices
Console.WriteLine("\n--- Audio Devices ---");
foreach (var mic in audio.GetInputDevices())
    Console.WriteLine($"  IN:  [{mic.DeviceId}] {mic.Name}");
foreach (var spk in audio.GetOutputDevices())
    Console.WriteLine($"  OUT: [{spk.DeviceId}] {spk.Name}");

// Select device by name (exact then contains, case-insensitive)
audio.SetInputDeviceByName("Realtek");
audio.SetOutputDeviceByName("Jabra");

// React to audio route changes (Bluetooth, headset, CarPlay, Android Auto)
audio.AudioRouteChanged += (s, e) =>
{
    Console.WriteLine($"Audio route changed: {e.Reason}, device: {e.NewDeviceName}");
};

// Make a call
if (phone.Accounts.Count > 0)
{
    Console.WriteLine("\nMaking a test call...");
    var call = phone.MakeCall(phone.Accounts[0], "sip:bob@example.com");
    Console.WriteLine($"Call state: {call.State}");
}

Console.WriteLine("Press Enter to stop...");
Console.ReadLine();

// Clean shutdown
await phone.StopAsync();
Console.WriteLine("Phone stopped.");
