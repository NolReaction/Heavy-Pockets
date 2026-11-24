#if UNITY_SERVER || UNITY_EDITOR || HEAVY_POCKETS_SERVER
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace HeavyPockets
{
    // One instance per dedicated process. NGO invokes the handlers on the simulation
    // thread, so one transfer finishes before the next request can claim an item.
    public sealed partial class RaidServer : IDisposable
    {
        private const float InputLifetime = 0.3f;
        private const float SnapshotInterval = 0.08f;
        private const float EvacuationTime = 3f;

        private sealed class Player
        {
            public ulong Id;
            public Vector2 Position;
            public Vector2 Direction;
            public Vector2 Velocity;
            public RaidLocomotion Motion;
            public MoveButtons Buttons;
            public float Yaw;
            public float Pitch, BoomLength, Aspect;
            public bool HasView, SnapCamera;
            public RaidCameraMode CameraMode;
            public bool HasShoulder, CheckTravelView;
            public float ShoulderPhase = 1f, ReportedPhase = 1f, TravelBoom;
            public RaidView View, TravelView;
            public float IdleSeconds = 1f;
            public float InputLeft;
            public float ExitSeconds;
            public bool Viewing;
            public readonly LootSearch Search = new LootSearch();
            public uint LastTransfer;
            public GearLocation ActiveGear = GearLocation.Primary;
            public bool Exited;
            public uint Round;
            public bool Entered, Queued;
            public bool AtBase;
            public byte PouchLevel = 1;
            public byte ReportedScaleLoad;
            public readonly HashSet<ulong> VisibleActors = new HashSet<ulong>();
            public float IngressCredits = 64f;
            public float MoveCredits = 12f;
            public float ActionCredits = 8f;
        }

        private readonly NetworkManager net;
        private readonly RaidAudit audit;
        private readonly Func<ulong, bool> mayPlay;
        private readonly bool pairedSpawn;
        private readonly Dictionary<ulong, Player> players = new Dictionary<ulong, Player>();
        private RaidLoot loot = new RaidLoot();
        private uint round = 1;
        private int nextSlot;
        private float untilSnapshot;

        public RaidServer(NetworkManager net, string auditPath = null, Func<ulong, bool> mayPlay = null, bool pairedSpawn = false)
        {
            if (!net.IsServer || (net.IsClient && !TrainingGround.Active))
                throw new InvalidOperationException("RaidServer requires a running dedicated server");
            this.net = net;
            this.mayPlay = mayPlay;
            this.pairedSpawn = pairedSpawn;
            WorldMechanisms.Reset();
            audit = RaidAudit.Open(auditPath);
            net.CustomMessagingManager.RegisterNamedMessageHandler(RaidWire.Commands, OnCommand);
            net.OnClientConnectedCallback += ActivatePlayer;
            net.OnClientDisconnectCallback += RemovePlayer;
            foreach (ulong id in net.ConnectedClientsIds)
                ActivatePlayer(id);
        }

        internal void ActivatePlayer(ulong id)
        {
            if ((id == NetworkManager.ServerClientId && !net.IsHost) || players.ContainsKey(id) ||
                !net.ConnectedClients.ContainsKey(id) || (mayPlay != null && !mayPlay(id)))
                return;
            if (players.Count >= 4)
            {
                net.DisconnectClient(id);
                return;
            }
            BeginRaid(id);
        }

        internal void RemovePlayer(ulong id)
        {
            if (!players.TryGetValue(id, out var player))
                return;
            players.Remove(id);
            if (!player.Exited && net.IsListening)
            {
                AnnounceDeparture(id);
                AdvanceRoundIfEmpty();
            }
        }

        private void BeginRaid(ulong id, Player previous = null)
        {
            if (!net.ConnectedClients.ContainsKey(id) || (mayPlay != null && !mayPlay(id))) return;
            // All per-raid input, visibility, search and extraction state start fresh.
            // Rate limits and the transfer sequence belong to the transport connection.
            int slot = nextSlot++;
            bool first = slot % 2 == 0;
            var player = new Player { Id = id, Round = round, Position = RaidMap.Spawn(slot), Yaw = first ? 51f : -109f };
            if (TrainingGround.Active) player.Yaw = -8f;
            if (pairedSpawn)
            {
                // Look across the open lane south of the three practice crates.
                player.Position = new Vector2(first ? -4f : -14.2f, -8.6f);
                player.Yaw = first ? -76f : 76f;
            }
            if (previous != null)
            {
                player.IngressCredits = previous.IngressCredits;
                player.MoveCredits = previous.MoveCredits;
                player.ActionCredits = previous.ActionCredits;
                player.LastTransfer = previous.LastTransfer;
                player.PouchLevel = previous.PouchLevel;
            }
            players[id] = player;
            loot.IssueKit(id, player.PouchLevel);
            Send(id, new RaidEvent { Kind = RaidEventKind.RaidStarted, Round = round });
            foreach (var passage in WorldMechanisms.Active) Send(id, PassageState(passage));
            Send(id, Pose(player));
            SendGear(player);
            SendInventory(player, RaidEventKind.Bag);
        }

        private void NextRaid(Player player, byte[] bytes)
        {
            if (bytes.Length != 6 || bytes[5] > 1 || !player.Exited || BitConverter.ToUInt32(bytes, 1) != player.Round)
                return;
            player.Queued = bytes[5] == 1;
            if (player.Queued && player.Round != round)
            {
                BeginRaid(player.Id, player);
                return;
            }
            Send(player.Id, new RaidEvent { Kind = RaidEventKind.RaidQueue, Round = player.Round, Queued = player.Queued });
        }

        private void AdvanceRoundIfEmpty()
        {
            if (nextSlot == 0) return;
            foreach (Player player in players.Values)
                if (!player.Exited) return;
            // Every result has already been sent. Retire the shared ledger once,
            // even if nobody has pressed the next-raid button yet.
            loot = new RaidLoot();
            WorldMechanisms.Reset();
            nextSlot = 0;
            round = round == uint.MaxValue ? 1 : round + 1;
            var ready = new List<Player>(players.Count);
            foreach (Player player in players.Values)
                if (player.Queued) ready.Add(player);
            foreach (Player player in ready)
                BeginRaid(player.Id, player);
        }

        private static RaidEvent Pose(Player player) => new RaidEvent
        {
            Kind = RaidEventKind.Pose, Actor = player.Id, Position = player.Position, Yaw = player.Yaw,
            Motion = player.Motion
        };

        private void SendInventory(Player player, RaidEventKind kind)
        {
            LootItem[] items;
            LootFootprint[] outlines = null;
            if (kind == RaidEventKind.Crate)
                loot.CrateView(player.Search.Identified, out items, out outlines);
            else
                items = kind == RaidEventKind.Result ? loot.ExtractedLoot(player.Id) : loot.Bag(player.Id);
            float remaining = outlines != null && outlines.Length > 0 ? player.Search.Remaining(loot) : 0f;
            Send(player.Id, new RaidEvent { Kind = kind, Items = items, Unknown = outlines, SearchRemaining = remaining });
        }

        private void Send(ulong id, RaidEvent message) =>
            RaidWire.Send(net, id, RaidWire.Events, RaidWire.Encode(message), audit);

        private void Status(Player player, RaidStatus status, uint operation = 0) =>
            Send(player.Id, new RaidEvent { Kind = RaidEventKind.Status, Status = status, Operation = operation });

        private void OnCommand(ulong sender, FastBufferReader reader)
        {
            if (!players.TryGetValue(sender, out var player))
                return;
            if (player.IngressCredits < 1f)
            {
                if (!net.IsHost || sender != net.LocalClientId) net.DisconnectClient(sender);
                return;
            }
            // Charge every incoming command before allocating or writing to the audit,
            // including malformed and unknown messages.
            player.IngressCredits -= 1f;
            byte[] bytes = RaidWire.Read(reader);
            if (bytes == null)
                return;
            audit?.Write("rx", sender, RaidWire.Commands, bytes);
            RaidCommand command = (RaidCommand)bytes[0];
            if (command == RaidCommand.EnterBase)
            {
                if (bytes.Length == 1 && player.Exited) player.AtBase = true;
                return;
            }
            if (command == RaidCommand.SetPouchLevel)
            {
                if (player.Exited && player.AtBase && !player.Queued && bytes.Length == 2 && bytes[1] >= 1 && bytes[1] <= 4)
                {
                    player.PouchLevel = bytes[1];
                    Send(player.Id, new RaidEvent { Kind = RaidEventKind.PouchLevel, PouchLevel = player.PouchLevel });
                }
                return;
            }
            if (command == RaidCommand.NextRaid)
            {
                NextRaid(player, bytes);
                return;
            }
            if (command == RaidCommand.EnterRaid)
            {
                if (!player.Exited && bytes.Length == 5 && BitConverter.ToUInt32(bytes, 1) == player.Round)
                    player.Entered = true;
                return;
            }
            // The round acknowledgement arrives on the same ordered channel as input.
            // Commands sent before the client saw the new start cannot affect this raid.
            if (!player.Entered && !player.Exited) return;
            if (command == RaidCommand.Move)
            {
                if (!player.Exited && player.MoveCredits >= 1f)
                {
                    player.MoveCredits -= 1f;
                    if (bytes.Length == 24)
                        ReadMovement(player, bytes);
                }
                return;
            }
            if (command == RaidCommand.MoveGear || command == RaidCommand.SelectGear)
            {
                GearCommand(player, command, bytes);
                return;
            }
            if (command == RaidCommand.SetMechanism)
            {
                if (!player.Exited && bytes.Length == 3 && WorldMechanisms.TryGet(bytes[1], out var passage) &&
                    bytes[2] <= 1 && player.ActionCredits >= 1f)
                {
                    player.ActionCredits -= 1f;
                    SetMechanism(player, passage, bytes[2] != 0);
                }
                return;
            }
            bool transfer = command == RaidCommand.Take || command == RaidCommand.Store;
            int expected = command == RaidCommand.Place ? 14 : transfer ? 9 : 1;
            if (bytes.Length != expected || bytes[0] < (byte)RaidCommand.Open ||
                bytes[0] > (byte)RaidCommand.Place)
                return;
            uint operation = (transfer || command == RaidCommand.Place) ? BitConverter.ToUInt32(bytes, 1) : 0;
            if (player.ActionCredits < 1f)
            {
                if (operation != 0) Status(player, RaidStatus.Denied, operation);
                return;
            }
            player.ActionCredits -= 1f;
            if (player.Exited)
            {
                Status(player, RaidStatus.AlreadyExited, operation);
                return;
            }
            switch (command)
            {
                case RaidCommand.Open:
                    if (!RaidMap.CanOpen(player.Position, player.Motion))
                    {
                        bool closeEnough = RaidMap.WithinReach(player.Position, player.Motion.Height);
                        Status(player, closeEnough ? RaidStatus.Blocked : RaidStatus.OutOfRange);
                        return;
                    }
                    if (!loot.CrateGenerated)
                    {
                        loot.GenerateCrate();
                        Debug.Log("HEAVY POCKETS CRATE GENERATED after authorized open");
                    }
                    player.Viewing = true;
                    Status(player, RaidStatus.Opened);
                    SendInventory(player, RaidEventKind.Crate);
                    break;
                case RaidCommand.Close:
                    player.Viewing = false;
                    Status(player, RaidStatus.Closed);
                    break;
                case RaidCommand.Take:
                case RaidCommand.Store:
                case RaidCommand.Place:
                    Transfer(player, command, bytes);
                    break;
            }
        }

        private static void ReadMovement(Player player, byte[] bytes)
        {
            float x = BitConverter.ToSingle(bytes, 1);
            float z = BitConverter.ToSingle(bytes, 5);
            float yaw = BitConverter.ToSingle(bytes, 9);
            float pitch = BitConverter.ToSingle(bytes, 13);
            float aspect = BitConverter.ToSingle(bytes, 18);
            byte sample = bytes[22];
            var direction = new Vector2(x, z);
            if (!RaidWire.Finite(x) || !RaidWire.Finite(z) || !RaidWire.Finite(yaw) ||
                direction.sqrMagnitude > 1.001f || Mathf.Abs(yaw) > 1000000f ||
                !RaidWire.Finite(pitch) || Mathf.Abs(pitch) > RaidCameraBoom.MaxPitch || bytes[17] > (byte)RaidCameraMode.LeftShoulder ||
                !RaidWire.Finite(aspect) || aspect < RaidCameraBoom.MinAspect || aspect > RaidCameraBoom.MaxAspect ||
                sample > RaidCameraBoom.LastSample || bytes[23] > 7 ||
                (bytes[17] == (byte)RaidCameraMode.FirstPerson && sample != RaidCameraBoom.CenterSample))
                return;
            player.Direction = Vector2.ClampMagnitude(direction, 1f);
            player.Yaw = Mathf.Repeat(yaw, 360f);
            MoveButtons next = (MoveButtons)bytes[23];
            player.Motion.Press(player.Position, player.Velocity, player.Yaw, player.Buttons, next);
            player.Buttons = next;
            player.Pitch = pitch;
            player.Aspect = aspect;
            var mode = (RaidCameraMode)bytes[17];
            player.SnapCamera |= !player.HasView ||
                (player.CameraMode == RaidCameraMode.FirstPerson) != (mode == RaidCameraMode.FirstPerson);
            if (mode != RaidCameraMode.FirstPerson)
            {
                player.ReportedPhase = sample / (float)RaidCameraBoom.LastSample;
                if (!player.HasShoulder)
                {
                    // A client may have kept its preferred shoulder from the preceding raid.
                    player.ShoulderPhase = sample == 0 ? 0f : 1f;
                    player.HasShoulder = true;
                }
            }
            player.CameraMode = mode;
            player.HasView = true;
            player.InputLeft = InputLifetime;
        }

        private static RaidEvent PassageState(WorldMechanism passage) => new RaidEvent
        {
            Kind = RaidEventKind.Mechanism, MechanismId = passage.Id, FromSide = passage.From,
            TargetSide = passage.Target, StartedAt = passage.Started
        };

        private void SetMechanism(Player operatorPlayer, WorldMechanism passage, bool open)
        {
            if (passage.Moving || open == passage.IsOpen ||
                passage != WorldMechanisms.Closest(operatorPlayer.Position, operatorPlayer.Motion)) return;
            int target = passage.NextTarget(operatorPlayer.Position);
            foreach (var player in players.Values)
                if (!player.Exited && passage.Sweeps(player.Position, player.Motion, target)) return;
            if (passage.ObstructedByBody(target)) return;
            passage.SetMotion(passage.Target, target, WorldMechanisms.Now);
            var state = PassageState(passage);
            foreach (var player in players.Values)
                if (!player.Exited) Send(player.Id, state);
        }

        private static void UpdateCamera(Player player, float delta)
        {
            // The reported camera must have reached a sightline, and the same sightline
            // must be open from the server's independently rate-limited trajectory.
            player.ShoulderPhase = RaidCameraBoom.AdvanceShoulder(player.ShoulderPhase, player.CameraMode, delta);
            Vector3 camera = RaidCameraBoom.Position(player.Position, player.Yaw, player.Pitch, player.CameraMode,
                player.Aspect, player.ReportedPhase, ref player.BoomLength, delta, player.SnapCamera, player.Motion.EyeOffset);
            Vector3 travel = RaidCameraBoom.Position(player.Position, player.Yaw, player.Pitch, player.CameraMode,
                player.Aspect, player.ShoulderPhase, ref player.TravelBoom, delta, player.SnapCamera, player.Motion.EyeOffset);
            player.SnapCamera = false;
            player.View = new RaidView(camera, player.Yaw, player.Pitch, player.Aspect);
            player.CheckTravelView = !camera.Equals(travel);
            if (player.CheckTravelView) player.TravelView = new RaidView(travel, player.Yaw, player.Pitch, player.Aspect);
        }

        private void Transfer(Player player, RaidCommand command, byte[] bytes)
        {
            uint sequence = BitConverter.ToUInt32(bytes, 1);
            if (sequence <= player.LastTransfer)
            {
                Status(player, RaidStatus.Stale, sequence);
                return;
            }
            player.LastTransfer = sequence;
            bool exact = command == RaidCommand.Place;
            byte flags = exact ? bytes[9] : (byte)0;
            if ((flags & 0xF0) != 0) { Status(player, RaidStatus.Denied, sequence); return; }
            bool fromBag = exact ? (flags & 1) != 0 : command == RaidCommand.Store;
            bool toBag = exact ? (flags & 2) != 0 : command == RaidCommand.Take;
            if ((!fromBag || !toBag) && (!player.Viewing || !RaidMap.CanOpen(player.Position, player.Motion)))
            {
                bool lostCrate = player.Viewing;
                player.Viewing = false;
                Status(player, RaidStatus.Denied, sequence);
                if (lostCrate) Status(player, RaidStatus.CrateLost);
                return;
            }
            uint id = BitConverter.ToUInt32(bytes, 5);
            RaidStatus status = loot.Transfer(player.Id, player.Search.Identified, id, fromBag, toBag, !exact,
                exact ? bytes[10] : 0, exact ? bytes[11] : 0, (flags & 4) != 0,
                exact ? bytes[12] : 0, exact ? bytes[13] : 0, (flags & 8) != 0);
            Status(player, status, sequence);
            if (status != RaidStatus.Placed && status != RaidStatus.Taken && status != RaidStatus.Stored) return;
            if (fromBag || toBag) SendInventory(player, RaidEventKind.Bag);
            if (fromBag && toBag) return;
            foreach (Player viewer in players.Values)
                if (viewer.Viewing && !viewer.Exited && RaidMap.CanOpen(viewer.Position, viewer.Motion))
                    SendInventory(viewer, RaidEventKind.Crate);
        }

        public void Tick(float deltaSeconds)
        {
            if (!net.IsListening || deltaSeconds <= 0f || !RaidWire.Finite(deltaSeconds))
                return;
            untilSnapshot -= deltaSeconds;
            bool snapshot = untilSnapshot <= 0f;
            if (snapshot)
                untilSnapshot = SnapshotInterval;
            bool hadExtraction = false;
            int scaleLoad = 0;
            foreach (Player player in players.Values)
            {
                player.IngressCredits = Mathf.Min(64f, player.IngressCredits + deltaSeconds * 60f);
                player.MoveCredits = Mathf.Min(12f, player.MoveCredits + deltaSeconds * 40f);
                player.ActionCredits = Mathf.Min(8f, player.ActionCredits + deltaSeconds * 6f);
                if (player.Exited)
                    continue;
                Vector2 before = player.Position;
                for (float left = deltaSeconds; left > 0f;)
                {
                    float step = Mathf.Min(left, 1f / 60f);
                    bool held = player.InputLeft > 0f;
                    player.Motion.Step(ref player.Position, ref player.Velocity, held ? player.Direction : Vector2.zero,
                        player.Yaw, held ? player.Buttons : MoveButtons.None, step);
                    player.InputLeft = Mathf.Max(0f, player.InputLeft - step);
                    left -= step;
                }
                player.IdleSeconds = (player.Position - before).sqrMagnitude > 0.00000001f ? 0f :
                    Mathf.Min(1f, player.IdleSeconds + deltaSeconds);
                UpdateCamera(player, deltaSeconds);
                if (player.Viewing && !RaidMap.CanOpen(player.Position, player.Motion))
                {
                    player.Viewing = false;
                    Status(player, RaidStatus.CrateLost);
                }
                if (player.Viewing && player.Search.Advance(loot, deltaSeconds))
                    SendInventory(player, RaidEventKind.Crate);
                UpdateExit(player, deltaSeconds, snapshot);
                hadExtraction |= player.Exited;
                if (!player.Exited && WarehouseFloorScale.Supports(player.Position, player.Motion))
                    scaleLoad += WarehouseFloorScale.RunnerMass;
            }
            if (hadExtraction) AdvanceRoundIfEmpty();
            ReportScaleLoad((byte)Mathf.Min(scaleLoad, WarehouseFloorScale.Capacity));
            foreach (Player actor in players.Values)
            {
                if (actor.Exited)
                    continue;
                byte[] pose = null;
                float modelYaw = actor.Velocity.sqrMagnitude > .09f
                    ? Mathf.Atan2(actor.Velocity.x, actor.Velocity.y) * Mathf.Rad2Deg
                    : actor.Yaw;
                foreach (Player recipient in players.Values)
                {
                    if (recipient.Exited)
                        continue;
                    bool send = snapshot;
                    if (recipient.Id != actor.Id)
                    {
                        if (!recipient.HasView || !actor.HasView ||
                            !RaidMap.CanSeeRunner(recipient.View, actor.Position, modelYaw, actor.IdleSeconds >= .45f, actor.Motion) ||
                            (recipient.CheckTravelView && !RaidMap.CanSeeRunner(recipient.TravelView, actor.Position, modelYaw, actor.IdleSeconds >= .45f, actor.Motion)))
                        {
                            HideActor(recipient, actor.Id);
                            continue;
                        }
                        if (recipient.VisibleActors.Add(actor.Id)) send = true;
                    }
                    if (send)
                        RaidWire.Send(net, recipient.Id, RaidWire.Events, pose ??= RaidWire.Encode(Pose(actor)), audit);
                }
            }
        }

        private void ReportScaleLoad(byte load)
        {
            foreach (Player player in players.Values)
            {
                if (player.Exited || (load == 0 && player.ReportedScaleLoad == 0)) continue;
                byte reading = load != 0 && WarehouseFloorScale.CanRead(player.Position, player.Motion) ? load : (byte)0;
                if (reading == player.ReportedScaleLoad) continue;
                player.ReportedScaleLoad = reading;
                Send(player.Id, new RaidEvent { Kind = RaidEventKind.ScaleLoad, ScaleKilograms = reading });
            }
        }

        private void UpdateExit(Player player, float deltaSeconds, bool snapshot)
        {
            bool wasInside = player.ExitSeconds > 0f;
            player.ExitSeconds = RaidMap.InExit(player.Position) ? player.ExitSeconds + deltaSeconds : 0f;
            if (player.ExitSeconds >= EvacuationTime)
            {
                player.Exited = true;
                player.Viewing = false;
                player.VisibleActors.Clear();
                SendInventory(player, RaidEventKind.Result);
                AnnounceDeparture(player.Id);
            }
            else if ((snapshot && player.ExitSeconds > 0f) || (wasInside && player.ExitSeconds == 0f))
            {
                Send(player.Id, new RaidEvent { Kind = RaidEventKind.ExitProgress, Seconds = player.ExitSeconds });
            }
        }

        private void AnnounceDeparture(ulong id)
        {
            foreach (Player recipient in players.Values)
                HideActor(recipient, id);
        }

        private void HideActor(Player recipient, ulong id)
        {
            if (recipient.VisibleActors.Remove(id))
                Send(recipient.Id, new RaidEvent { Kind = RaidEventKind.Left, Actor = id });
        }

        public void Dispose()
        {
            if (net != null)
            {
                net.OnClientConnectedCallback -= ActivatePlayer;
                net.OnClientDisconnectCallback -= RemovePlayer;
                net.CustomMessagingManager?.UnregisterNamedMessageHandler(RaidWire.Commands);
            }
            audit?.Dispose();
        }
    }
}
#endif
