#if HEAVY_POCKETS_EOS
using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace HeavyPockets
{
    // Only the protected player builds compile this transport binding. EOS control
    // traffic is separate from the gameplay protocol and its optional payload audit.
    internal sealed class EosRaidNetwork : MonoBehaviour
    {
        private const string Channel = "hp.eos.control.v1";
        private const byte Identity = 1, Registered = 2, Attestation = 3;
        private const int MaxToken = 8192, MaxMessage = 512;
        private NetworkManager net;
        private EosRaidIdentity identity;
        private EosRaidAntiCheat protection;
        private readonly Queue<byte[]> earlyMessages = new Queue<byte[]>();
        private bool remoteReady, stopping;
        private string userId, token, failureMessage;
        private float startTime;
#if UNITY_SERVER || HEAVY_POCKETS_SERVER
        private sealed class Peer
        {
            public float Deadline, BudgetTime, Credits = 128f;
            public string ProductUser;
            public bool Verifying, Registered, Authorized, Removing;
        }
        private readonly Dictionary<ulong, Peer> peers = new Dictionary<ulong, Peer>();
        private readonly HashSet<string> productUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<ulong> expired = new List<ulong>();
        private RaidServer raid;
#endif
        private static string Required(string name)
        {
            string value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("Missing protected configuration: " + name);
            Environment.SetEnvironmentVariable(name, null);
            return value;
        }

        internal void Begin(NetworkManager manager, UnityTransport transport, ushort port)
        {
            net = manager;
            startTime = Time.realtimeSinceStartup;
            transport.UseEncryption = true;
#if UNITY_SERVER || HEAVY_POCKETS_SERVER
            string bind = Environment.GetEnvironmentVariable("HP_EOS_BIND_ADDRESS") ?? "127.0.0.1";
            transport.SetConnectionData(true, "127.0.0.1", port, bind);
            transport.SetServerSecrets(Required("HP_RAID_CERT"), Required("HP_RAID_KEY"));
            identity = EosRaidIdentity.Create(true);
            protection = EosRaidAntiCheat.StartServer(identity.Platform, (peer, data) => Send(peer, Attestation, data), Authorize, Revoke);
            net.ConnectionApprovalCallback = (request, response) =>
            {
                response.Approved = peers.Count < 4 && (request.Payload == null || request.Payload.Length == 0);
                response.CreatePlayerObject = false;
                if (response.Approved)
                    peers.Add(request.ClientNetworkId, new Peer { Deadline = Time.realtimeSinceStartup + 110f, BudgetTime = Time.realtimeSinceStartup });
            };
            net.OnClientDisconnectCallback += Depart;
            if (!net.StartServer()) throw new InvalidOperationException("Protected server failed to start.");
            net.CustomMessagingManager.RegisterNamedMessageHandler(Channel, Receive);
            raid = new RaidServer(net, null, id => peers.TryGetValue(id, out var peer) && peer.Authorized && !peer.Removing);
            Debug.Log("Heavy Pockets protected server awaiting EOS-verifiable players.");
#else
            string address = Environment.GetEnvironmentVariable("HP_EOS_CONNECT_ADDRESS") ?? "127.0.0.1";
            transport.SetConnectionData(true, address, port);
            transport.SetClientSecrets(Required("HP_EOS_TLS_HOST"), Required("HP_RAID_CA"));
            identity = EosRaidIdentity.Create(false);
            identity.Login((puid, jwt) =>
            {
                if (stopping) return;
                try
                {
                    userId = puid.ToString();
                    token = jwt;
                    protection = EosRaidAntiCheat.StartClient(identity.Platform, puid, ToServer, StopProtected);
                    net.OnClientConnectedCallback += Connected;
                    net.OnClientDisconnectCallback += ClientDisconnected;
                    if (!net.StartClient()) throw new InvalidOperationException("Protected client failed to start.");
                    net.CustomMessagingManager.RegisterNamedMessageHandler(Channel, Receive);
                    gameObject.AddComponent<RaidClient>().Initialize(net, null);
                }
                catch { StopProtected(); }
            }, StopProtected);
#endif
        }

#if UNITY_SERVER || HEAVY_POCKETS_SERVER
        private void Authorize(ulong id)
        {
            if (!peers.TryGetValue(id, out var peer) || peer.Removing || !peer.Registered) return;
            peer.Authorized = true;
            raid.ActivatePlayer(id);
        }

        private void Revoke(ulong id, string reason)
        {
            if (!peers.TryGetValue(id, out var peer) || peer.Removing) return;
            peer.Removing = true;
            peer.Authorized = false;
            Debug.Log("Protected raid removed connection " + id + ": " + (reason ?? "Invalid or expired session."));
            // Do not wait for the NGO disconnect callback to end gameplay.
            raid?.RemovePlayer(id);
            net.DisconnectClient(id, "Protected session ended.");
        }

        private void Depart(ulong id)
        {
            if (!peers.TryGetValue(id, out var peer)) return;
            peers.Remove(id);
            if (peer.ProductUser != null) productUsers.Remove(peer.ProductUser);
            if (peer.Registered) protection.UnregisterPeer(id);
        }

        private void Verify(ulong id, Peer peer, byte[] payload)
        {
            // Fixed-width 32-character Product User ID, followed by the EOS Connect JWT.
            if (peer.Verifying || payload.Length <= 32) { Revoke(id, null); return; }
            peer.Verifying = true;
            string claimed = Encoding.ASCII.GetString(payload, 0, 32);
            string jwt = Encoding.ASCII.GetString(payload, 32, payload.Length - 32);
            identity.Verify(claimed, jwt, verified =>
            {
                if (stopping || !peers.TryGetValue(id, out var current) || current != peer || peer.Removing) return;
                string canonical = verified.ToString();
                if (!productUsers.Add(canonical)) { Revoke(id, null); return; }
                peer.ProductUser = canonical;
                try
                {
                    peer.Registered = true;
                    protection.RegisterPeer(id, verified);
                    Send(id, Registered, Array.Empty<byte>());
                }
                catch { Revoke(id, null); }
            }, reason => Revoke(id, null));
        }
#else
        private void Connected(ulong id)
        {
            if (id != net.LocalClientId || userId == null || token == null) { StopProtected(); return; }
            var frame = Encoding.ASCII.GetBytes(userId + token);
            token = null;
            if (userId.Length != 32 || frame.Length > MaxToken) { StopProtected(); return; }
            Send(NetworkManager.ServerClientId, Identity, frame);
            Array.Clear(frame, 0, frame.Length);
        }

        private void ClientDisconnected(ulong id) => StopProtected();

        private void ToServer(byte[] payload)
        {
            if (stopping) return;
            if (payload == null || payload.Length < 1 || payload.Length > MaxMessage) { StopProtected(); return; }
            if (remoteReady) Send(NetworkManager.ServerClientId, Attestation, payload);
            else if (earlyMessages.Count < 64) earlyMessages.Enqueue((byte[])payload.Clone());
            else StopProtected();
        }
#endif
        private void Send(ulong id, byte kind, byte[] payload)
        {
            if (stopping || payload == null) return;
            using (var writer = new FastBufferWriter(1 + payload.Length, Allocator.Temp))
            {
                writer.WriteByteSafe(kind);
                if (payload.Length != 0) writer.WriteBytesSafe(payload, payload.Length);
                net.CustomMessagingManager.SendNamedMessage(Channel, id, writer, NetworkDelivery.ReliableFragmentedSequenced);
            }
        }

        private void Receive(ulong sender, FastBufferReader reader)
        {
            try
            {
                int count = reader.Length - reader.Position - 1;
                if (count < 0 || count > MaxToken) throw new FormatException();
                reader.ReadByteSafe(out byte kind);
#if UNITY_SERVER || HEAVY_POCKETS_SERVER
                if (!peers.TryGetValue(sender, out var peer) || peer.Removing) return;
                float now = Time.realtimeSinceStartup;
                peer.Credits = Mathf.Min(128f, peer.Credits + (now - peer.BudgetTime) * 64f);
                peer.BudgetTime = now;
                if (peer.Credits < 1f || (kind == Identity ? peer.Verifying || count <= 32 :
                    kind != Attestation || !peer.Registered || count < 1 || count > MaxMessage)) throw new FormatException();
                peer.Credits -= 1f;
                var data = new byte[count];
                reader.ReadBytesSafe(ref data, count);
                if (kind == Identity) Verify(sender, peer, data);
                else protection.ReceiveFromPeer(sender, data);
#else
                if (sender != NetworkManager.ServerClientId) return;
                if (kind == Registered && count == 0 && !remoteReady)
                {
                    remoteReady = true;
                    while (earlyMessages.Count > 0) ToServer(earlyMessages.Dequeue());
                }
                else if (kind == Attestation && count > 0 && count <= MaxMessage)
                {
                    var data = new byte[count];
                    reader.ReadBytesSafe(ref data, count);
                    protection.ReceiveFromServer(data);
                }
                else throw new FormatException();
#endif
            }
            catch
            {
#if UNITY_SERVER || HEAVY_POCKETS_SERVER
                Revoke(sender, null);
#else
                StopProtected();
#endif
            }
        }

        private void Update()
        {
            if (stopping) return;
            try
            {
                identity?.Tick();
#if UNITY_SERVER || HEAVY_POCKETS_SERVER
                expired.Clear();
                foreach (var entry in peers)
                    if (!entry.Value.Authorized && !entry.Value.Removing && entry.Value.Deadline <= Time.realtimeSinceStartup)
                        expired.Add(entry.Key);
                foreach (ulong id in expired) Revoke(id, null);
#else
                if (!remoteReady && Time.realtimeSinceStartup - startTime > 115f) StopProtected();
#endif
            }
            catch { StopProtected(); }
        }

#if UNITY_SERVER || HEAVY_POCKETS_SERVER
        private void FixedUpdate() { if (!stopping) raid?.Tick(Time.fixedDeltaTime); }
#endif
        private void StopProtected(string reason = null)
        {
            if (stopping) return;
            stopping = true;
            failureMessage = string.IsNullOrWhiteSpace(reason) ? "Сервер завершил защищённую сессию." : reason;
            Debug.LogError("EOS protected session unavailable or closed.");
            if (net != null && net.IsListening) net.Shutdown();
            if (Application.isBatchMode) Application.Quit(3);
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void OnGUI()
        {
            if (failureMessage == null || Application.isBatchMode) return;
            GUI.depth = -100;
            float w = Mathf.Min(460, Screen.width - 24);
            var area = new Rect((Screen.width - w) / 2f, (Screen.height - 180) / 2f, w, 180);
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label("Heavy Pockets · Easy Anti-Cheat");
            GUILayout.Space(10);
            GUILayout.Label(failureMessage);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Закрыть игру")) Application.Quit(3);
            GUILayout.EndArea();
        }

        private void OnDestroy()
        {
            stopping = true;
            if (net != null)
            {
                net.CustomMessagingManager?.UnregisterNamedMessageHandler(Channel);
#if UNITY_SERVER || HEAVY_POCKETS_SERVER
                net.OnClientDisconnectCallback -= Depart;
                raid?.Dispose();
#else
                net.OnClientConnectedCallback -= Connected;
                net.OnClientDisconnectCallback -= ClientDisconnected;
#endif
            }
            try { protection?.Dispose(); } finally { identity?.Dispose(); }
            earlyMessages.Clear();
            token = null;
        }
    }
}
#endif
