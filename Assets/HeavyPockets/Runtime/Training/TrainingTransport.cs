#if UNITY_EDITOR
using System;
using Unity.Netcode;
using UnityEngine;

namespace HeavyPockets
{
    // NGO dispatches named messages to its own host directly. This editor-only transport
    // has no external endpoint and needs neither a server binary nor a free UDP port.
    internal sealed class TrainingTransport : NetworkTransport
    {
        public override ulong ServerClientId => NetworkManager.ServerClientId;
        public override void Initialize(NetworkManager networkManager = null) { }
        public override bool StartServer() => true;
        public override bool StartClient() => false;
        public override void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery delivery) { }
        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
        {
            clientId = 0;
            payload = default;
            receiveTime = 0f;
            return NetworkEvent.Nothing;
        }
        public override ulong GetCurrentRtt(ulong clientId) => 0;
        public override void DisconnectRemoteClient(ulong clientId) { }
        public override void DisconnectLocalClient() { }
        public override void Shutdown() { }
    }
}
#endif
