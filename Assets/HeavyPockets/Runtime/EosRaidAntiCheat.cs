#if HEAVY_POCKETS_EOS
using System;
using System.Collections.Generic;
using Epic.OnlineServices;
using Epic.OnlineServices.Platform;
using C = Epic.OnlineServices.AntiCheatClient;
using S = Epic.OnlineServices.AntiCheatServer;
using AC = Epic.OnlineServices.AntiCheatCommon;

namespace HeavyPockets
{
    // Call on the EOS platform thread. The platform and its Tick loop belong
    // to the caller. Game authorization comes only from the server callback.
    internal sealed class EosRaidAntiCheat : IDisposable
    {
        private readonly Dictionary<ulong, ProductUserId> peers = new Dictionary<ulong, ProductUserId>();
        private readonly HashSet<ulong> verified = new HashSet<ulong>();
        private readonly HashSet<ulong> removed = new HashSet<ulong>();
        private C.AntiCheatClientInterface client;
        private S.AntiCheatServerInterface server;
        private Action<ulong, byte[]> sendToPeer;
        private Action<byte[]> sendToServer;
        private Action<ulong> onVerified;
        private Action<ulong, string> revokePeer;
        private Action<string> onIntegrityFailure;
        private ulong messageNotice, statusNotice, actionNotice, integrityNotice;
        private bool started, disposed;

        private EosRaidAntiCheat() { }

        internal static EosRaidAntiCheat StartServer(PlatformInterface platform, Action<ulong, byte[]> sendToPeer,
            Action<ulong> onVerified, Action<ulong, string> revokePeer)
        {
            var ac = new EosRaidAntiCheat
            {
                server = platform.GetAntiCheatServerInterface() ?? throw new InvalidOperationException("EOS Anti-Cheat Server unavailable."),
                sendToPeer = sendToPeer ?? throw new ArgumentNullException(nameof(sendToPeer)),
                onVerified = onVerified ?? throw new ArgumentNullException(nameof(onVerified)),
                revokePeer = revokePeer ?? throw new ArgumentNullException(nameof(revokePeer))
            };
            try
            {
                var message = new S.AddNotifyMessageToClientOptions();
                var status = new S.AddNotifyClientAuthStatusChangedOptions();
                var action = new S.AddNotifyClientActionRequiredOptions();
                ac.messageNotice = RequireNotice(ac.server.AddNotifyMessageToClient(ref message, ac, ac.ServerMessage));
                ac.statusNotice = RequireNotice(ac.server.AddNotifyClientAuthStatusChanged(ref status, ac, ac.AuthStatus));
                ac.actionNotice = RequireNotice(ac.server.AddNotifyClientActionRequired(ref action, ac, ac.RequiredAction));
                var options = new S.BeginSessionOptions { ServerName = "Heavy Pockets", RegisterTimeoutSeconds = 60 };
                Require(ac.server.BeginSession(ref options), "Begin dedicated EAC session");
                ac.started = true;
                return ac;
            }
            catch { ac.Dispose(); throw; }
        }

        internal static EosRaidAntiCheat StartClient(PlatformInterface platform, ProductUserId localUser,
            Action<byte[]> sendToServer, Action<string> onIntegrityFailure)
        {
            if (localUser == null || !localUser.IsValid()) throw new ArgumentException("Expected a signed-in EOS user.");
            var ac = new EosRaidAntiCheat
            {
                client = platform.GetAntiCheatClientInterface() ?? throw new InvalidOperationException("EOS Anti-Cheat Client unavailable."),
                sendToServer = sendToServer ?? throw new ArgumentNullException(nameof(sendToServer)),
                onIntegrityFailure = onIntegrityFailure ?? throw new ArgumentNullException(nameof(onIntegrityFailure))
            };
            try
            {
                var message = new C.AddNotifyMessageToServerOptions();
                var integrity = new C.AddNotifyClientIntegrityViolatedOptions();
                ac.messageNotice = RequireNotice(ac.client.AddNotifyMessageToServer(ref message, ac, ac.ClientMessage));
                ac.integrityNotice = RequireNotice(ac.client.AddNotifyClientIntegrityViolated(ref integrity, ac, ac.IntegrityFailure));
                var options = new C.BeginSessionOptions { LocalUserId = localUser, Mode = C.AntiCheatClientMode.ClientServer };
                Require(ac.client.BeginSession(ref options), "Begin protected EAC client session");
                ac.started = true;
                return ac;
            }
            catch { ac.Dispose(); throw; }
        }

        internal void RegisterPeer(ulong peer, ProductUserId verifiedPuid)
        {
            if (!started || server == null || peer == 0 || peer > long.MaxValue ||
                verifiedPuid == null || !verifiedPuid.IsValid() || peers.ContainsKey(peer) || peers.ContainsValue(verifiedPuid))
                throw new InvalidOperationException("Invalid or duplicate protected peer.");
            var options = new S.RegisterClientOptions
            {
                ClientHandle = new IntPtr((long)peer), ClientType = AC.AntiCheatCommonClientType.ProtectedClient,
                ClientPlatform = AC.AntiCheatCommonClientPlatform.Windows, UserId = verifiedPuid
            };
            peers.Add(peer, verifiedPuid);
            try { Require(server.RegisterClient(ref options), "Register protected client"); }
            catch { peers.Remove(peer); throw; }
        }

        internal void UnregisterPeer(ulong peer)
        {
            if (!peers.Remove(peer)) return;
            verified.Remove(peer);
            removed.Remove(peer);
            var options = new S.UnregisterClientOptions { ClientHandle = new IntPtr((long)peer) };
            Require(server.UnregisterClient(ref options), "Unregister protected client");
        }

        internal void ReceiveFromPeer(ulong peer, byte[] payload)
        {
            if (!started || !peers.ContainsKey(peer) || removed.Contains(peer))
                throw new InvalidOperationException("Unregistered EAC sender.");
            var options = new S.ReceiveMessageFromClientOptions
            {
                ClientHandle = new IntPtr((long)peer),
                Data = Packet(payload, C.AntiCheatClientInterface.ONMESSAGETOSERVERCALLBACK_MAX_MESSAGE_SIZE)
            };
            Require(server.ReceiveMessageFromClient(ref options), "Receive protected client data");
        }

        internal void ReceiveFromServer(byte[] payload)
        {
            if (!started || client == null) throw new InvalidOperationException("Inactive EAC client.");
            var options = new C.ReceiveMessageFromServerOptions
            {
                Data = Packet(payload, S.AntiCheatServerInterface.ONMESSAGETOCLIENTCALLBACK_MAX_MESSAGE_SIZE)
            };
            Require(client.ReceiveMessageFromServer(ref options), "Receive protected server data");
        }

        private static ArraySegment<byte> Packet(byte[] payload, int max)
        {
            if (payload == null || payload.Length == 0 || payload.Length > max)
                throw new ArgumentException("Invalid EAC frame size.");
            return new ArraySegment<byte>(payload);
        }

        private void ClientMessage(ref C.OnMessageToServerCallbackInfo data)
        {
            if (!disposed) sendToServer(data.MessageData.ToArray());
        }

        private void ServerMessage(ref AC.OnMessageToClientCallbackInfo data)
        {
            ulong peer = (ulong)data.ClientHandle.ToInt64();
            if (!disposed && peers.ContainsKey(peer) && !removed.Contains(peer))
                sendToPeer(peer, data.MessageData.ToArray());
        }

        private void AuthStatus(ref AC.OnClientAuthStatusChangedCallbackInfo data)
        {
            ulong peer = (ulong)data.ClientHandle.ToInt64();
            if (disposed || !peers.ContainsKey(peer) || removed.Contains(peer)) return;
            if (data.ClientAuthStatus == AC.AntiCheatCommonClientAuthStatus.RemoteAuthComplete)
            {
                if (verified.Add(peer)) onVerified(peer);
            }
            else if (verified.Contains(peer))
                Remove(peer, "EAC remote authentication lost.");
        }

        private void RequiredAction(ref AC.OnClientActionRequiredCallbackInfo data)
        {
            if (!disposed && data.ClientAction == AC.AntiCheatCommonClientAction.RemovePlayer)
                Remove((ulong)data.ClientHandle.ToInt64(), "EAC: " + data.ActionReasonCode);
        }

        private void Remove(ulong peer, string reason)
        {
            if (peers.ContainsKey(peer) && removed.Add(peer))
            {
                verified.Remove(peer);
                revokePeer(peer, reason);
            }
        }

        private void IntegrityFailure(ref C.OnClientIntegrityViolatedCallbackInfo data)
        {
            if (!disposed) onIntegrityFailure("EAC: " + data.ViolationType + ". " + data.ViolationMessage);
        }

        private static ulong RequireNotice(ulong id) => id != Common.INVALID_NOTIFICATIONID ? id :
            throw new InvalidOperationException("EOS anti-cheat callback registration failed.");

        private static void Require(Result result, string operation)
        {
            if (result != Result.Success) throw new InvalidOperationException(operation + ": " + result);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (server != null)
            {
                foreach (ulong peer in peers.Keys)
                {
                    var unregister = new S.UnregisterClientOptions { ClientHandle = new IntPtr((long)peer) };
                    server.UnregisterClient(ref unregister);
                }
                peers.Clear();
                if (started) { var end = new S.EndSessionOptions(); server.EndSession(ref end); }
                if (actionNotice != 0) server.RemoveNotifyClientActionRequired(actionNotice);
                if (statusNotice != 0) server.RemoveNotifyClientAuthStatusChanged(statusNotice);
                if (messageNotice != 0) server.RemoveNotifyMessageToClient(messageNotice);
            }
            if (client != null)
            {
                if (started) { var end = new C.EndSessionOptions(); client.EndSession(ref end); }
                if (integrityNotice != 0) client.RemoveNotifyClientIntegrityViolated(integrityNotice);
                if (messageNotice != 0) client.RemoveNotifyMessageToServer(messageNotice);
            }
            started = false;
        }
    }
}
#endif
