using UnityEngine;

/// <summary>
/// Serializable, per-port interface config (IP/mask/administrative shutdown
/// state) for a Router — the persistent home for what RouterCLI's
/// "ip address"/"shutdown"/"no shutdown" commands configure. Living on the
/// GameObject itself (rather than only inside RouterCLI's in-memory session)
/// is what lets a router's interface config actually survive: SceneStateCapture
/// reads/writes it for the player's own save file, and a level-template scene
/// (hand-built or produced by the "Save Play Topology As Level Template" Editor
/// tool) carries it as plain Inspector-visible data, the same way DeviceAddress
/// already does for single-IP devices. Attached automatically to every router
/// by DeviceModelBuilder.BuildRouter() (see EnsureSize).
/// </summary>
public class RouterInterfaceConfig : MonoBehaviour
{
    [System.Serializable]
    public class PortConfig
    {
        public string ip = "";       // "" = unassigned
        public string mask = "";     // "" = unassigned
        public bool shutdown = true; // real IOS default: interfaces start administratively down
        public long addressedAtTicks; // when this interface joined its subnet — see DeviceAddress.AddressedAtTicks
    }

    [SerializeField] private PortConfig[] ports = new PortConfig[0];

    public int Count => ports.Length;

    /// <summary>Grows/shrinks the array to match the device's current port count,
    /// preserving any existing entries — safe to call repeatedly (every router
    /// build/restore path calls this rather than assuming the array is already
    /// the right size).</summary>
    public void EnsureSize(int portCount)
    {
        if (ports.Length == portCount) return;
        var resized = new PortConfig[portCount];
        for (int i = 0; i < portCount; i++)
            resized[i] = i < ports.Length ? ports[i] : new PortConfig();
        ports = resized;
    }

    public PortConfig Get(int index) => (index >= 0 && index < ports.Length) ? ports[index] : null;
}
