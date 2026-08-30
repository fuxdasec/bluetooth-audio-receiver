namespace BluetoothAudioReceiver.Core;

public enum ConnectionState
{
    Disabled,
    WaitingForDevice,
    Enabling,
    Connecting,
    Connected,
    Recovering,
}

