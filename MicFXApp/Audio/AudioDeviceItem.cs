using NAudio.CoreAudioApi;

namespace MicFXApp.Audio
{
    /// <summary>
    /// Wraps an MMDevice with its FriendlyName resolved once, up front.
    /// MMDevice.FriendlyName triggers a COM property-store lookup on every
    /// access - fine once, but if a ComboBox's binding re-resolves it during
    /// dropdown layout (and there are many endpoints, which is common on
    /// modern PCs with several HDMI/DP outputs, virtual devices, etc.), that
    /// adds up to a visible stall right when the dropdown opens. Resolving it
    /// once here, off the UI thread at load time, makes the dropdown itself
    /// just bind to a plain cached string - instant.
    /// </summary>
    public class AudioDeviceItem
    {
        public MMDevice Device { get; }
        public string FriendlyName { get; }

        public AudioDeviceItem(MMDevice device)
        {
            Device = device;
            FriendlyName = device.FriendlyName;
        }

        public override string ToString() => FriendlyName;
    }
}
