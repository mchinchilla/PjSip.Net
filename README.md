# PjSip.Net

SDK de alto nivel para integrar telefonía SIP en aplicaciones .NET 10. Basado en [PJSIP 2.16](https://www.pjsip.org/) con soporte TLS nativo (Schannel en Windows), compatible con **WinForms**, **WPF**, **MAUI** y **Console**.

## Tabla de Contenidos

- [Requisitos](#requisitos)
- [Instalación](#instalación)
- [Inicio Rápido](#inicio-rápido)
- [Configuración](#configuración)
  - [SipPhoneOptions](#sipphoneoptions)
  - [SipAccountOptions](#sipaccountoptions)
  - [Transporte y TLS](#transporte-y-tls)
- [Inyección de Dependencias](#inyección-de-dependencias)
- [API Reference](#api-reference)
  - [ISipPhone](#isipphone)
  - [ISipAccount](#isipaccount)
  - [ISipCall](#isipcall)
  - [ISipAudioManager](#isipaudiomanager)
- [Eventos](#eventos)
- [Ejemplos por Plataforma](#ejemplos-por-plataforma)
  - [Console App](#console-app)
  - [WPF](#wpf)
  - [WinForms](#winforms)
  - [MAUI](#maui)
- [Manejo de Errores](#manejo-de-errores)
- [Audio](#audio)
- [Acceso Low-Level (pjsua2)](#acceso-low-level-pjsua2)
- [Arquitectura](#arquitectura)
- [Plataformas Soportadas](#plataformas-soportadas)
- [Build desde Código Fuente](#build-desde-código-fuente)

---

## Requisitos

- **.NET 10** SDK o superior
- **Paquete nativo** correspondiente a tu plataforma (se instala automáticamente via NuGet)

## Instalación

```bash
# SDK principal (obligatorio)
dotnet add package PjSip.Net

# Binarios nativos — instala el de tu plataforma objetivo
dotnet add package PjSip.Net.Native.Win64        # Windows x64
dotnet add package PjSip.Net.Native.MacOS         # macOS x64 / arm64
dotnet add package PjSip.Net.Native.Android       # Android arm64
dotnet add package PjSip.Net.Native.iOS           # iOS arm64
```

> **Nota:** El paquete nativo contiene el binario compilado de `pjsua2` y se copia automáticamente al directorio de salida.

---

## Inicio Rápido

```csharp
using Microsoft.Extensions.DependencyInjection;
using PjSip.Net;
using PjSip.Net.Accounts;
using PjSip.Net.DependencyInjection;
using PjSip.Net.Transport;

// 1. Configurar servicios
var services = new ServiceCollection();
services.AddLogging();
services.AddPjSip(options =>
{
    options.Transports.Add(new SipTransportOptions
    {
        Type = SipTransportType.Udp,
        Port = 5060
    });
    options.Accounts.Add(new SipAccountOptions
    {
        Username = "1001",
        Password = "secret",
        Domain = "pbx.miempresa.com",
        Registrar = "sip:pbx.miempresa.com"
    });
});

// 2. Resolver y arrancar
var provider = services.BuildServiceProvider();
var phone = provider.GetRequiredService<ISipPhone>();

phone.IncomingCall += (s, e) =>
{
    Console.WriteLine($"Llamada entrante de {e.RemoteUri}");
    e.Call.Answer();  // Contestar automáticamente
};

phone.CallStateChanged += (s, e) =>
    Console.WriteLine($"Llamada {e.Call.Id}: {e.OldState} -> {e.NewState}");

await phone.StartAsync();

// 3. Realizar una llamada
var call = phone.MakeCall(phone.Accounts[0], "sip:1002@pbx.miempresa.com");

// 4. Colgar
call.Hangup();

// 5. Apagar limpiamente
await phone.StopAsync();
```

---

## Configuración

### SipPhoneOptions

Opciones globales del endpoint SIP. Se configuran al registrar el servicio.

```csharp
services.AddPjSip(options =>
{
    options.UserAgent = "MiApp/2.0";        // User-Agent en headers SIP (default: "PjSip.Net/1.0")
    options.LogLevel = 4;                    // Nivel de log PJSIP: 0=fatal, 5=trace (default: 4)
    options.MaxCalls = 8;                    // Máximo de llamadas simultáneas (default: 4)
    options.UseCompactForm = false;          // Headers SIP compactos (default: false)
    options.Transports = [ ... ];            // Lista de transportes a crear
    options.Accounts = [ ... ];             // Cuentas a registrar al iniciar
});
```

| Propiedad | Tipo | Default | Descripción |
|---|---|---|---|
| `UserAgent` | `string` | `"PjSip.Net/1.0"` | Valor del header User-Agent en mensajes SIP |
| `LogLevel` | `int` | `4` | Verbosidad del log interno de PJSIP (0-5) |
| `MaxCalls` | `int` | `4` | Número máximo de llamadas simultáneas |
| `UseCompactForm` | `bool` | `false` | Usar headers SIP en forma compacta |
| `Transports` | `List<SipTransportOptions>` | `[]` | Transportes SIP a crear al iniciar |
| `Accounts` | `List<SipAccountOptions>` | `[]` | Cuentas SIP a registrar automáticamente |

### SipAccountOptions

Configuración de una cuenta SIP individual.

```csharp
new SipAccountOptions
{
    Username = "1001",                       // Usuario SIP (obligatorio)
    Password = "secret",                     // Contraseña (obligatorio)
    Domain = "pbx.miempresa.com",           // Dominio SIP (obligatorio)
    Registrar = "sip:pbx.miempresa.com",    // URI del registrar (null = usa Domain)
    DisplayName = "Juan Pérez",             // Nombre para mostrar en caller ID
    Realm = "*",                             // Realm de autenticación (null = automático)
    RegistrationTimeout = 300,               // Expiración del registro en segundos (default: 300)
    RegisterOnAdd = true                     // Registrar automáticamente al agregar (default: true)
}
```

| Propiedad | Tipo | Default | Descripción |
|---|---|---|---|
| `Username` | `string` | *requerido* | Usuario SIP para autenticación |
| `Password` | `string` | *requerido* | Contraseña de la cuenta |
| `Domain` | `string` | *requerido* | Dominio/servidor SIP |
| `Registrar` | `string?` | `null` | URI completa del registrar. Si es `null`, se construye desde `Domain` |
| `DisplayName` | `string?` | `null` | Nombre visible en el Caller ID |
| `Realm` | `string?` | `null` | Realm para digest auth. `null` = acepta cualquier challenge |
| `RegistrationTimeout` | `int` | `300` | Tiempo de expiración del REGISTER en segundos |
| `RegisterOnAdd` | `bool` | `true` | Si `true`, envía REGISTER automáticamente al agregar la cuenta |

### Transporte y TLS

```csharp
// UDP (sin cifrar)
options.Transports.Add(new SipTransportOptions
{
    Type = SipTransportType.Udp,
    Port = 5060
});

// TCP
options.Transports.Add(new SipTransportOptions
{
    Type = SipTransportType.Tcp,
    Port = 5060
});

// TLS (cifrado) — Usa Schannel en Windows, sin dependencia de OpenSSL
options.Transports.Add(new SipTransportOptions
{
    Type = SipTransportType.Tls,
    Port = 5061,
    Tls = new TlsOptions
    {
        VerifyServer = true,               // Validar certificado del servidor (default: true)
        VerifyClient = false,              // Requiere certificado del cliente (default: false)
        CertificateFile = null,            // Ruta al certificado del cliente (.pem)
        PrivateKeyFile = null,             // Ruta a la clave privada del cliente (.pem)
        CaListFile = null                  // Ruta a CAs de confianza adicionales (.pem)
    }
});

// IPv6
options.Transports.Add(new SipTransportOptions
{
    Type = SipTransportType.Tls6,          // TLS sobre IPv6
    Port = 5061
});
```

**Tipos de transporte disponibles:**

| Enum | Protocolo | Puerto Estándar |
|---|---|---|
| `SipTransportType.Udp` | UDP/IPv4 | 5060 |
| `SipTransportType.Tcp` | TCP/IPv4 | 5060 |
| `SipTransportType.Tls` | TLS/IPv4 | 5061 |
| `SipTransportType.Udp6` | UDP/IPv6 | 5060 |
| `SipTransportType.Tcp6` | TCP/IPv6 | 5060 |
| `SipTransportType.Tls6` | TLS/IPv6 | 5061 |

**TlsOptions:**

| Propiedad | Tipo | Default | Descripción |
|---|---|---|---|
| `VerifyServer` | `bool` | `true` | Validar el certificado TLS del servidor |
| `VerifyClient` | `bool` | `false` | Requerir certificado TLS del cliente |
| `CertificateFile` | `string?` | `null` | Ruta al certificado del cliente (formato PEM) |
| `PrivateKeyFile` | `string?` | `null` | Ruta a la clave privada del cliente (formato PEM) |
| `CaListFile` | `string?` | `null` | Ruta a la lista de CAs de confianza adicionales |

> **Windows:** TLS usa Schannel (nativo del OS). No necesitas instalar OpenSSL.
> **macOS/iOS:** Usa Secure Transport del sistema.
> **Android:** Requiere OpenSSL precompilado (incluido en el paquete nativo).

---

## Inyección de Dependencias

### Registro básico (Singleton)

```csharp
services.AddPjSip(options =>
{
    options.Transports.Add(new SipTransportOptions { Type = SipTransportType.Udp });
    options.Accounts.Add(new SipAccountOptions
    {
        Username = "1001",
        Password = "secret",
        Domain = "pbx.miempresa.com"
    });
});
```

### Registro con lifetime explícito

```csharp
using PjSip.Net.DependencyInjection;

// Singleton (default) — una instancia para toda la aplicación
services.AddPjSip(options => { ... }, PjSipServiceLifetime.Singleton);

// Scoped — una instancia por scope (útil en aplicaciones web)
services.AddPjSip(options => { ... }, PjSipServiceLifetime.Scoped);
```

### Servicios registrados

`AddPjSip` registra automáticamente:

| Servicio | Descripción |
|---|---|
| `ISipPhone` | Facade principal — gestión de cuentas, llamadas y transporte |
| `ISipAudioManager` | Gestión de dispositivos de audio (micrófono, altavoz, volumen) |

### Inyectar en tus clases

```csharp
public class MiServicioTelefonia
{
    private readonly ISipPhone _phone;

    public MiServicioTelefonia(ISipPhone phone)
    {
        _phone = phone;
        _phone.IncomingCall += OnIncomingCall;
    }

    public async Task IniciarAsync()
    {
        await _phone.StartAsync();
    }

    public ISipCall Llamar(string destino)
    {
        return _phone.MakeCall(_phone.Accounts[0], destino);
    }

    private void OnIncomingCall(object? sender, IncomingCallEventArgs e)
    {
        // Lógica para llamadas entrantes
    }
}
```

---

## API Reference

### ISipPhone

Facade principal del SDK. Gestiona el ciclo de vida del endpoint SIP, cuentas y llamadas.

```csharp
public interface ISipPhone : IAsyncDisposable, IDisposable
```

**Propiedades:**

| Propiedad | Tipo | Descripción |
|---|---|---|
| `State` | `SipPhoneState` | Estado actual del teléfono |
| `Accounts` | `IReadOnlyList<ISipAccount>` | Cuentas SIP registradas |
| `Audio` | `ISipAudioManager` | Gestor de dispositivos de audio |

**Métodos:**

| Método | Retorno | Descripción |
|---|---|---|
| `StartAsync(ct)` | `Task` | Inicializa PJSIP, crea transportes y registra cuentas configuradas |
| `StopAsync(ct)` | `Task` | Cuelga todas las llamadas, des-registra cuentas y destruye el endpoint |
| `AddAccount(options)` | `ISipAccount` | Agrega una nueva cuenta SIP en runtime |
| `RemoveAccount(account)` | `void` | Elimina y des-registra una cuenta |
| `MakeCall(account, uri)` | `ISipCall` | Inicia una llamada saliente desde una cuenta |

**Estados (`SipPhoneState`):**

| Estado | Descripción |
|---|---|
| `Idle` | Recién creado, no inicializado |
| `Starting` | Inicializando el endpoint PJSIP |
| `Running` | Operativo — puede hacer y recibir llamadas |
| `Stopping` | Apagándose |
| `Stopped` | Apagado limpiamente |
| `Error` | Error durante inicio o parada |

---

### ISipAccount

Representa una cuenta SIP con la que se pueden enviar/recibir llamadas.

```csharp
public interface ISipAccount : IDisposable
```

**Propiedades:**

| Propiedad | Tipo | Descripción |
|---|---|---|
| `Id` | `string` | Identificador único de la cuenta |
| `Uri` | `string` | URI SIP de la cuenta (ej: `sip:1001@pbx.com`) |
| `RegistrationState` | `SipRegistrationState` | Estado de registro actual |
| `Options` | `SipAccountOptions` | Configuración de la cuenta |
| `ActiveCalls` | `IReadOnlyList<ISipCall>` | Llamadas activas en esta cuenta |

**Métodos:**

| Método | Retorno | Descripción |
|---|---|---|
| `RegisterAsync(ct)` | `Task` | Envía REGISTER al servidor |
| `UnregisterAsync(ct)` | `Task` | Envía un-REGISTER al servidor |
| `MakeCall(destinationUri)` | `ISipCall` | Inicia una llamada desde esta cuenta |

**Estados de registro (`SipRegistrationState`):**

| Estado | Descripción |
|---|---|
| `Unregistered` | No registrado |
| `Registering` | REGISTER enviado, esperando respuesta |
| `Registered` | Registrado exitosamente (200 OK) |
| `Unregistering` | Un-REGISTER enviado |
| `Error` | Error en el registro (401, 403, timeout, etc.) |

---

### ISipCall

Representa una llamada SIP activa (entrante o saliente).

```csharp
public interface ISipCall : IDisposable
```

**Propiedades:**

| Propiedad | Tipo | Descripción |
|---|---|---|
| `Id` | `string` | Identificador único de la llamada |
| `State` | `SipCallState` | Estado actual de la llamada |
| `Direction` | `CallDirection` | `Incoming` o `Outgoing` |
| `Info` | `SipCallInfo` | Información detallada (URIs, duración, status code) |

**Métodos:**

| Método | Descripción |
|---|---|
| `Answer(statusCode)` | Contestar la llamada. Default: `200` (OK) |
| `Hangup(statusCode)` | Colgar la llamada. Default: `603` (Decline) |
| `Hold()` | Poner en espera (hold) |
| `Unhold()` | Quitar de espera (re-INVITE) |
| `Transfer(destinationUri)` | Transferir la llamada a otro destino (REFER) |
| `SendDtmf(digits)` | Enviar tonos DTMF (ej: `"1234#"`) |
| `SetMute(mute)` | Silenciar/des-silenciar el micrófono |

**Códigos de respuesta comunes para `Answer()`:**

| Código | Significado |
|---|---|
| `180` | Ringing (sin contestar, solo señalizar ring) |
| `200` | OK — contestar la llamada |
| `486` | Busy Here — rechazar como ocupado |
| `603` | Decline — rechazar la llamada |

**Estados de llamada (`SipCallState`):**

| Estado | Descripción |
|---|---|
| `Null` | Llamada recién creada |
| `Calling` | INVITE enviado, esperando respuesta |
| `Incoming` | INVITE recibido, sin contestar |
| `EarlyMedia` | Recibiendo early media (183 + SDP) |
| `Connecting` | Respuesta 2xx recibida, estableciendo media |
| `Confirmed` | Llamada activa con audio bidireccional |
| `Disconnected` | Llamada terminada |

**SipCallInfo (información de la llamada):**

| Propiedad | Tipo | Descripción |
|---|---|---|
| `CallId` | `string` | Call-ID del header SIP |
| `RemoteUri` | `string` | URI del otro extremo |
| `LocalUri` | `string` | URI local |
| `State` | `SipCallState` | Estado actual |
| `Direction` | `CallDirection` | Dirección de la llamada |
| `Duration` | `TimeSpan` | Duración de la llamada |
| `RemoteDisplayName` | `string?` | Nombre del llamante remoto |
| `StatusCode` | `int` | Último código SIP recibido |
| `StatusText` | `string?` | Texto del último status SIP |

---

### ISipAudioManager

Gestión de dispositivos de audio y volumen.

```csharp
public interface ISipAudioManager
```

**Propiedades:**

| Propiedad | Tipo | Descripción |
|---|---|---|
| `CurrentInputDevice` | `AudioDeviceInfo?` | Micrófono activo actual |
| `CurrentOutputDevice` | `AudioDeviceInfo?` | Altavoz/auricular activo actual |
| `InputLevel` | `float` | Nivel de volumen de entrada (0.0 — 1.0) |
| `OutputLevel` | `float` | Nivel de volumen de salida (0.0 — 1.0) |

**Métodos:**

| Método | Retorno | Descripción |
|---|---|---|
| `GetInputDevices()` | `IReadOnlyList<AudioDeviceInfo>` | Lista de micrófonos disponibles |
| `GetOutputDevices()` | `IReadOnlyList<AudioDeviceInfo>` | Lista de altavoces disponibles |
| `SetInputDevice(deviceId)` | `void` | Cambiar el micrófono activo |
| `SetOutputDevice(deviceId)` | `void` | Cambiar el altavoz activo |

**AudioDeviceInfo:**

| Propiedad | Tipo | Descripción |
|---|---|---|
| `DeviceId` | `int` | ID del dispositivo (para usar con `SetInputDevice`/`SetOutputDevice`) |
| `Name` | `string` | Nombre del dispositivo (ej: "Realtek HD Audio") |
| `InputChannels` | `int` | Número de canales de entrada |
| `OutputChannels` | `int` | Número de canales de salida |
| `Driver` | `string?` | Nombre del driver de audio |

---

## Eventos

Todos los eventos se disparan en el hilo que procesó el callback de PJSIP. En aplicaciones UI (WinForms/WPF/MAUI), usa el dispatcher correspondiente para actualizar la interfaz.

### En ISipPhone (nivel global)

```csharp
// Llamada entrante en cualquier cuenta
phone.IncomingCall += (sender, e) =>
{
    Console.WriteLine($"Llamada de {e.RemoteDisplayName} <{e.RemoteUri}>");
    Console.WriteLine($"Cuenta destino: {e.Account.Uri}");

    e.Call.Answer();           // Contestar
    // o: e.Call.Hangup(486);  // Rechazar como ocupado
};

// Cambio de estado en cualquier llamada
phone.CallStateChanged += (sender, e) =>
{
    Console.WriteLine($"Llamada {e.Call.Id}: {e.OldState} -> {e.NewState}");

    if (e.NewState == SipCallState.Disconnected)
        Console.WriteLine("Llamada finalizada");
};

// Cambio de registro en cualquier cuenta
phone.RegistrationStateChanged += (sender, e) =>
{
    Console.WriteLine($"Cuenta {e.Account.Uri}: {e.OldState} -> {e.NewState}");

    if (e.NewState == SipRegistrationState.Error)
        Console.WriteLine($"Error de registro: {e.StatusCode} {e.Reason}");
};

// Cambio de estado del transporte
phone.TransportStateChanged += (sender, e) =>
{
    Console.WriteLine($"Transporte {e.TransportType}: {e.State}");
};
```

### En ISipAccount (nivel cuenta)

```csharp
var account = phone.Accounts[0];

account.RegistrationStateChanged += (sender, e) =>
    Console.WriteLine($"Mi cuenta: {e.NewState}");

account.IncomingCall += (sender, e) =>
    Console.WriteLine($"Llamada entrante para esta cuenta: {e.RemoteUri}");
```

### En ISipCall (nivel llamada)

```csharp
var call = phone.MakeCall(account, "sip:1002@pbx.com");

call.StateChanged += (sender, e) =>
{
    Console.WriteLine($"Estado: {e.OldState} -> {e.NewState}");

    if (e.NewState == SipCallState.Confirmed)
        Console.WriteLine("Audio activo!");
};

call.MediaStateChanged += (sender, e) =>
{
    Console.WriteLine($"Media activa: {e.IsActive}");
};
```

---

## Ejemplos por Plataforma

### Console App

```csharp
using Microsoft.Extensions.DependencyInjection;
using PjSip.Net;
using PjSip.Net.Accounts;
using PjSip.Net.DependencyInjection;
using PjSip.Net.Transport;

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
        Username = "1001",
        Password = "secret",
        Domain = "pbx.miempresa.com",
        Registrar = "sip:pbx.miempresa.com"
    });
});

var provider = services.BuildServiceProvider();
var phone = provider.GetRequiredService<ISipPhone>();

phone.IncomingCall += (s, e) =>
{
    Console.WriteLine($"Llamada de {e.RemoteUri}");
    e.Call.Answer();
};

await phone.StartAsync();
Console.WriteLine("Teléfono activo. Presiona Enter para salir...");
Console.ReadLine();
await phone.StopAsync();
```

### WPF

```csharp
// En App.xaml.cs o con un HostBuilder
public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override async void OnStartup(StartupEventArgs e)
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
                Username = "1001",
                Password = "secret",
                Domain = "pbx.miempresa.com"
            });
        });

        _serviceProvider = services.BuildServiceProvider();
        var mainWindow = new MainWindow(_serviceProvider.GetRequiredService<ISipPhone>());
        mainWindow.Show();
    }
}

// En MainWindow.xaml.cs
public partial class MainWindow : Window
{
    private readonly ISipPhone _phone;
    private ISipCall? _activeCall;

    public MainWindow(ISipPhone phone)
    {
        InitializeComponent();
        _phone = phone;

        // IMPORTANTE: Usar Dispatcher para actualizar UI desde eventos SIP
        _phone.IncomingCall += (s, e) =>
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = $"Llamada entrante de {e.RemoteDisplayName}";
                // Mostrar diálogo de aceptar/rechazar
            });

        _phone.CallStateChanged += (s, e) =>
            Dispatcher.Invoke(() =>
                StatusText.Text = $"Llamada: {e.NewState}");
    }

    private async void OnStartClick(object sender, RoutedEventArgs e)
    {
        await _phone.StartAsync();
        StatusText.Text = $"Conectado ({_phone.Accounts.Count} cuentas)";
    }

    private void OnCallClick(object sender, RoutedEventArgs e)
    {
        _activeCall = _phone.MakeCall(_phone.Accounts[0], DestinationBox.Text);
    }

    private void OnHangupClick(object sender, RoutedEventArgs e)
    {
        _activeCall?.Hangup();
        _activeCall = null;
    }
}
```

### WinForms

```csharp
public partial class MainForm : Form
{
    private ISipPhone? _phone;

    private async Task StartPhoneAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPjSip(options =>
        {
            options.Transports.Add(new SipTransportOptions { Type = SipTransportType.Udp });
            options.Accounts.Add(new SipAccountOptions
            {
                Username = "1001",
                Password = "secret",
                Domain = "pbx.miempresa.com"
            });
        });

        var provider = services.BuildServiceProvider();
        _phone = provider.GetRequiredService<ISipPhone>();

        // IMPORTANTE: Usar BeginInvoke para actualizar UI
        _phone.IncomingCall += (s, e) =>
            BeginInvoke(() =>
                MessageBox.Show($"Llamada de {e.RemoteUri}", "Llamada Entrante"));

        _phone.CallStateChanged += (s, e) =>
            BeginInvoke(() =>
                lblStatus.Text = $"Llamada: {e.NewState}");

        await _phone.StartAsync();
        lblStatus.Text = "Conectado";
    }

    private void btnCall_Click(object sender, EventArgs e)
    {
        _phone?.MakeCall(_phone.Accounts[0], txtDestination.Text);
    }
}
```

### MAUI

```csharp
// En MauiProgram.cs
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        builder.Services.AddPjSip(options =>
        {
            options.Transports.Add(new SipTransportOptions
            {
                Type = SipTransportType.Udp
            });
            options.Accounts.Add(new SipAccountOptions
            {
                Username = "1001",
                Password = "secret",
                Domain = "pbx.miempresa.com"
            });
        });

        return builder.Build();
    }
}

// En una página
public partial class PhonePage : ContentPage
{
    private readonly ISipPhone _phone;

    public PhonePage(ISipPhone phone)
    {
        InitializeComponent();
        _phone = phone;

        // MAUI: Usar MainThread.BeginInvokeOnMainThread para UI
        _phone.IncomingCall += (s, e) =>
            MainThread.BeginInvokeOnMainThread(() =>
                StatusLabel.Text = $"Llamada de {e.RemoteUri}");
    }

    private async void OnStartClicked(object? sender, EventArgs e)
    {
        await _phone.StartAsync();
        StatusLabel.Text = "Conectado";
    }

    private void OnCallClicked(object? sender, EventArgs e)
    {
        _phone.MakeCall(_phone.Accounts[0], DestinationEntry.Text);
    }
}
```

---

## Manejo de Errores

El SDK define excepciones específicas para errores SIP:

```csharp
using PjSip.Net.Exceptions;

try
{
    await phone.StartAsync();
}
catch (SipTransportException ex)
{
    // Error al crear el transporte (puerto ocupado, TLS mal configurado, etc.)
    Console.WriteLine($"Error de transporte: {ex.Message} (código PJSIP: {ex.PjStatusCode})");
}
catch (PjSipException ex)
{
    // Error genérico de PJSIP
    Console.WriteLine($"Error PJSIP: {ex.Message} (código: {ex.PjStatusCode})");
}

// Errores de registro se notifican via evento
phone.RegistrationStateChanged += (s, e) =>
{
    if (e.NewState == SipRegistrationState.Error)
    {
        // e.StatusCode contiene el código SIP (401, 403, 408, etc.)
        Console.WriteLine($"Error de registro: {e.StatusCode} - {e.Reason}");
    }
};
```

**Jerarquía de excepciones:**

```
PjSipException                    Base — cualquier error de PJSIP
├── SipRegistrationException      Error de REGISTER (4xx, 5xx)
└── SipTransportException         Error de transporte (bind, TLS, red)
```

---

## Audio

```csharp
var audio = phone.Audio;

// Listar dispositivos
var microphones = audio.GetInputDevices();
var speakers = audio.GetOutputDevices();

foreach (var mic in microphones)
    Console.WriteLine($"[{mic.DeviceId}] {mic.Name} ({mic.InputChannels}ch)");

foreach (var spk in speakers)
    Console.WriteLine($"[{spk.DeviceId}] {spk.Name} ({spk.OutputChannels}ch)");

// Cambiar dispositivo
audio.SetInputDevice(microphones[1].DeviceId);
audio.SetOutputDevice(speakers[0].DeviceId);

// Ajustar volumen (0.0 = silencio, 1.0 = máximo)
audio.InputLevel = 0.8f;    // Micrófono al 80%
audio.OutputLevel = 1.0f;   // Altavoz al 100%

// Silenciar una llamada específica
call.SetMute(true);   // Silenciar micrófono en esta llamada
call.SetMute(false);  // Des-silenciar
```

---

## Acceso Low-Level (pjsua2)

Para escenarios avanzados que requieran acceso directo a las clases de pjsua2 generadas por SWIG:

```csharp
// Las clases SWIG están en el namespace PjSip.Net.Interop.Generated
using PjSip.Net.Interop.Generated;

// Ejemplo: acceder al endpoint nativo directamente
// (disponible una vez que los wrappers SWIG estén generados)
```

> **Nota:** El acceso low-level requiere conocimiento de la API de pjsua2. Consulta la [documentación oficial de PJSIP](https://docs.pjsip.org/).

---

## Arquitectura

```
Tu Aplicación (WinForms / WPF / MAUI / Console)
    │
    ├── PjSip.Net                    SDK de alto nivel
    │   ├── ISipPhone                  Facade principal
    │   ├── ISipAccount                Gestión de cuentas
    │   ├── ISipCall                   Control de llamadas
    │   ├── ISipAudioManager           Dispositivos de audio
    │   ├── DI (AddPjSip)             Inyección de dependencias
    │   └── Events                     Eventos .NET estándar
    │       │
    │       └── PjSip.Net.Interop    Capa de interop (SWIG)
    │           ├── NativeLoader       Carga de librería nativa cross-platform
    │           └── Generated/         Clases C# generadas por SWIG
    │               │
    │               └── [DllImport("pjsua2")]  ──►  pjsua2 nativo
    │
    └── PjSip.Net.Native.{Platform}  Binarios nativos por plataforma
        └── runtimes/{rid}/native/     pjsua2.dll / libpjsua2.dylib / .so
```

**Design Patterns utilizados:**

| Pattern | Uso |
|---|---|
| **Facade** | `ISipPhone` como entry point único |
| **Options** | `SipPhoneOptions`, `SipAccountOptions` via `IOptions<T>` |
| **Observer** | Eventos .NET (`IncomingCall`, `CallStateChanged`, etc.) |
| **Factory** | `AddAccount()`, `MakeCall()` |
| **Adapter** | `ManagedAccount`/`ManagedCall` adaptan callbacks pjsua2 a eventos .NET |
| **Dispose** | Limpieza en cascada de recursos nativos |

---

## Plataformas Soportadas

| Plataforma | RID | TLS Backend | Paquete Nativo |
|---|---|---|---|
| Windows x64 | `win-x64` | Schannel | `PjSip.Net.Native.Win64` |
| macOS x64 | `osx-x64` | OpenSSL | `PjSip.Net.Native.MacOS` |
| macOS ARM64 | `osx-arm64` | OpenSSL | `PjSip.Net.Native.MacOS` |
| Android ARM64 | `android-arm64` | OpenSSL | `PjSip.Net.Native.Android` |
| iOS ARM64 | `ios-arm64` | Secure Transport | `PjSip.Net.Native.iOS` |

---

## Build desde Código Fuente

### Prerequisitos

- .NET 10 SDK
- Visual Studio 2022 con workload C++ (para compilar pjsua2 en Windows)
- SWIG 4.0+ (para generar wrappers C#)

### Compilar la solución managed

```bash
dotnet build netpjsip.slnx
dotnet test tests/PjSip.Net.Tests.Unit/PjSip.Net.Tests.Unit.csproj
```

### Compilar binarios nativos

```powershell
# Windows x64 (PowerShell)
./native/scripts/build-win-x64.ps1

# macOS (bash)
./native/scripts/build-macos-arm64.sh   # Apple Silicon
./native/scripts/build-macos-x64.sh     # Intel

# Mobile (bash)
./native/scripts/build-android-arm64.sh
./native/scripts/build-ios-arm64.sh
```

### Generar wrappers SWIG

```powershell
./native/scripts/generate-swig.ps1
```

### Crear paquetes NuGet

```bash
dotnet pack src/PjSip.Net/PjSip.Net.csproj -o ./artifacts
dotnet pack src/PjSip.Net.Interop/PjSip.Net.Interop.csproj -o ./artifacts
dotnet pack src/PjSip.Net.Native.Win64/PjSip.Net.Native.Win64.csproj -o ./artifacts
```

---

## Licencia

MIT
