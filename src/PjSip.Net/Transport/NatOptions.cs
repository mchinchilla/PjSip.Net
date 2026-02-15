namespace PjSip.Net.Transport;

public sealed class NatOptions
{
    public bool EnableStun { get; set; }
    public List<string> StunServers { get; set; } = [];
    public bool EnableIce { get; set; } = true;
    public bool EnableTurn { get; set; }
    public string? TurnServer { get; set; }
    public string? TurnUsername { get; set; }
    public string? TurnPassword { get; set; }
    public NatTraversalType TurnTransport { get; set; } = NatTraversalType.Udp;
    public bool IceAggressiveNomination { get; set; }
}
