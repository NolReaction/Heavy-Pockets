using System.Text;
using System;
using System.IO;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace HeavyPockets
{
    public enum RaidEventKind : byte { Pose = 1, Left, Crate, Bag, Result, Status, ExitProgress, RaidStarted, RaidQueue, Gear, PouchLevel, WarehouseDoor, ScaleLoad }
    public enum RaidStatus : byte { Opened = 1, Blocked, Closed, Taken, Empty, Denied, OutOfRange, AlreadyExited, CrateLost, Stored, BagFull, Placed, NoSpace, Stale }
    internal enum RaidCommand : byte { Move = 1, Open, Close, Take, Store, Place, NextRaid, EnterRaid, MoveGear, SelectGear, SetPouchLevel, EnterBase, SetWarehouseDoor }

    public struct RaidEvent
    {
        public RaidEventKind Kind;
        public ulong Actor;
        public Vector2 Position;
        public float Yaw;
        public RaidLocomotion Motion;
        public float Seconds;
        public LootItem[] Items, Gear;
        public GearLocation ActiveGear;
        public bool SearchComplete;
        public float SearchRemaining;
        public LootFootprint[] Unknown;
        public RaidStatus Status;
        public uint Operation;
        public uint Round;
        public bool Queued;
        public bool DoorOpen;
        public byte ScaleKilograms;
        public byte PouchLevel;
    }

    // The Windows prototype uses little-endian wire values. Each message contains only
    // the fields for its own kind. Public poses cannot serialize any inventory fields.
    internal static class RaidWire
    {
        public const string Commands = "heavypockets.command.v12";
        public const string Events = "heavypockets.event.v12";
        private const int ResultCapacity = LootCatalog.BagCapacity + GearCatalog.Slots - 1;
        private const int MaxPayload = 3 + ResultCapacity * 8;

        private static int ItemCapacity(RaidEventKind kind) => kind == RaidEventKind.Crate
            ? LootCatalog.CrateCells : kind == RaidEventKind.Result ? ResultCapacity : LootCatalog.BagCapacity;

        public static void Put(byte[] bytes, int offset, float value)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(value), 0, bytes, offset, sizeof(float));
        }

        private static void PutActor(byte[] bytes, ulong value)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(value), 0, bytes, 1, sizeof(ulong));
        }

        public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public static byte[] Read(FastBufferReader reader)
        {
            int count = reader.Length - reader.Position;
            if (count < 1 || count > MaxPayload)
                return null;
            var bytes = new byte[count];
            reader.ReadBytesSafe(ref bytes, count);
            return bytes;
        }

        public static void Send(NetworkManager net, ulong peer, string name, byte[] bytes, RaidAudit audit)
        {
            using (var writer = new FastBufferWriter(bytes.Length, Allocator.Temp))
            {
                writer.WriteBytesSafe(bytes, bytes.Length);
                net.CustomMessagingManager.SendNamedMessage(name, peer, writer, NetworkDelivery.ReliableSequenced);
                // Snapshot the actual writer submitted to NGO. RX logs use the other process's reader.
                if (audit != null)
                    audit.Write("tx", peer, name, writer.ToArray());
            }
        }

        public static byte[] Encode(RaidEvent message)
        {
            byte[] bytes;
            switch (message.Kind)
            {
                case RaidEventKind.Pose:
                    bytes = new byte[31];
                    PutActor(bytes, message.Actor);
                    Put(bytes, 9, message.Position.x);
                    Put(bytes, 13, message.Position.y);
                    Put(bytes, 17, message.Yaw);
                    Put(bytes, 21, message.Motion.Height);
                    Put(bytes, 25, message.Motion.VerticalSpeed);
                    bytes[29] = (byte)Mathf.RoundToInt(Mathf.Clamp01(message.Motion.Crouch) * 255f);
                    bytes[30] = (byte)((message.Motion.Sliding ? 2 : message.Motion.Sprinting ? 1 : 0) |
                        (message.Motion.Airborne ? 4 : 0));
                    break;
                case RaidEventKind.Left:
                    bytes = new byte[9];
                    PutActor(bytes, message.Actor);
                    break;
                case RaidEventKind.WarehouseDoor:
                    bytes = new byte[] { 0, (byte)(message.DoorOpen ? 1 : 0) };
                    break;
                case RaidEventKind.ScaleLoad:
                    bytes = new byte[] { 0, message.ScaleKilograms };
                    break;
                case RaidEventKind.Crate:
                case RaidEventKind.Bag:
                case RaidEventKind.Result:
                    LootItem[] items = message.Items;
                    LootFootprint[] unknown = message.Unknown ?? Array.Empty<LootFootprint>();
                    if (items == null || items.Length + unknown.Length > ItemCapacity(message.Kind) ||
                        (message.Kind != RaidEventKind.Crate && unknown.Length != 0))
                        throw new ArgumentException("Invalid loot snapshot.");
                    int layoutLength = 3 + 8 * items.Length + 4 * unknown.Length;
                    bytes = new byte[layoutLength + (unknown.Length > 0 ? 2 : 0)];
                    if (unknown.Length > 0)
                    {
                        ushort remainingMs = (ushort)Mathf.Clamp(Mathf.CeilToInt(message.SearchRemaining * 1000f),
                            1, Mathf.RoundToInt(LootCatalog.SearchItemSeconds * 1000f));
                        Buffer.BlockCopy(BitConverter.GetBytes(remainingMs), 0, bytes, layoutLength, 2);
                    }
                    bytes[1] = (byte)unknown.Length;
                    bytes[2] = (byte)items.Length;
                    for (int i = 0; i < items.Length; i++)
                    {
                        int offset = 3 + i * 8;
                        Buffer.BlockCopy(BitConverter.GetBytes(items[i].Id), 0, bytes, offset, 4);
                        bytes[offset + 4] = items[i].Type;
                        bytes[offset + 5] = items[i].X;
                        bytes[offset + 6] = items[i].Y;
                        bytes[offset + 7] = (byte)(items[i].Rotated ? 1 : 0);
                    }
                    for (int i = 0; i < unknown.Length; i++)
                    {
                        int offset = 3 + items.Length * 8 + i * 4;
                        bytes[offset] = unknown[i].X;
                        bytes[offset + 1] = unknown[i].Y;
                        bytes[offset + 2] = unknown[i].Width;
                        bytes[offset + 3] = unknown[i].Height;
                    }
                    break;
                case RaidEventKind.Gear:
                    if (message.Gear == null || message.Gear.Length != GearCatalog.Slots)
                        throw new ArgumentException("Invalid equipment snapshot");
                    bytes = new byte[2 + 5 * (GearCatalog.Slots - 1)];
                    bytes[1] = (byte)message.ActiveGear;
                    for (int i = 1; i < GearCatalog.Slots; i++)
                    {
                        int offset = 2 + 5 * (i - 1);
                        Buffer.BlockCopy(BitConverter.GetBytes(message.Gear[i].Id), 0, bytes, offset, 4);
                        bytes[offset + 4] = message.Gear[i].Type;
                    }
                    break;
                case RaidEventKind.Status:
                    bytes = new byte[6];
                    bytes[1] = (byte)message.Status;
                    Buffer.BlockCopy(BitConverter.GetBytes(message.Operation), 0, bytes, 2, 4);
                    break;
                case RaidEventKind.PouchLevel:
                    if (message.PouchLevel < 1 || message.PouchLevel > 4)
                        throw new ArgumentOutOfRangeException(nameof(message.PouchLevel));
                    bytes = new byte[] { 0, message.PouchLevel };
                    break;
                case RaidEventKind.ExitProgress:
                    bytes = new byte[5];
                    Put(bytes, 1, message.Seconds);
                    break;
                case RaidEventKind.RaidStarted:
                case RaidEventKind.RaidQueue:
                    if (message.Round == 0) throw new ArgumentOutOfRangeException(nameof(message.Round));
                    bytes = new byte[message.Kind == RaidEventKind.RaidStarted ? 5 : 6];
                    Buffer.BlockCopy(BitConverter.GetBytes(message.Round), 0, bytes, 1, 4);
                    if (message.Kind == RaidEventKind.RaidQueue) bytes[5] = message.Queued ? (byte)1 : (byte)0;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(message));
            }
            bytes[0] = (byte)message.Kind;
            return bytes;
        }

        public static bool TryDecode(byte[] bytes, out RaidEvent message)
        {
            message = default;
            if (bytes == null || bytes.Length == 0)
                return false;
            message.Kind = (RaidEventKind)bytes[0];
            switch (message.Kind)
            {
                case RaidEventKind.Pose when bytes.Length == 31:
                    message.Actor = BitConverter.ToUInt64(bytes, 1);
                    message.Position = new Vector2(BitConverter.ToSingle(bytes, 9), BitConverter.ToSingle(bytes, 13));
                    message.Yaw = BitConverter.ToSingle(bytes, 17);
                    message.Motion = new RaidLocomotion { Height = BitConverter.ToSingle(bytes, 21),
                        VerticalSpeed = BitConverter.ToSingle(bytes, 25), Crouch = bytes[29] / 255f,
                        Sprinting = (bytes[30] & 1) != 0, Sliding = (bytes[30] & 2) != 0,
                        Airborne = (bytes[30] & 4) != 0 };
                    return Finite(message.Position.x) && Finite(message.Position.y) && Finite(message.Yaw) &&
                        message.Motion.Height >= 0f && message.Motion.Height <= 64f &&
                        Mathf.Abs(message.Motion.VerticalSpeed) <= RaidLocomotion.TerminalSpeed && bytes[30] <= 5 &&
                        (message.Motion.Airborne || message.Motion.VerticalSpeed == 0f) &&
                        (!message.Motion.Sliding || !message.Motion.Sprinting);
                case RaidEventKind.Left when bytes.Length == 9:
                    message.Actor = BitConverter.ToUInt64(bytes, 1);
                    return true;
                case RaidEventKind.WarehouseDoor when bytes.Length == 2 && bytes[1] <= 1:
                    message.DoorOpen = bytes[1] != 0;
                    return true;
                case RaidEventKind.ScaleLoad when bytes.Length == 2:
                    message.ScaleKilograms = bytes[1];
                    return bytes[1] == 0 || bytes[1] == WarehouseFloorScale.RunnerMass ||
                        bytes[1] == 2 * WarehouseFloorScale.RunnerMass || bytes[1] == WarehouseFloorScale.Capacity;
                case RaidEventKind.Crate:
                case RaidEventKind.Bag:
                case RaidEventKind.Result:
                    if (bytes.Length < 3 || bytes.Length != 3 + 8 * bytes[2] + 4 * bytes[1] + (bytes[1] > 0 ? 2 : 0) ||
                        bytes[1] + bytes[2] > ItemCapacity(message.Kind))
                        return false;
                    bool crate = message.Kind == RaidEventKind.Crate;
                    if (!crate && bytes[1] != 0) return false;
                    message.SearchComplete = bytes[1] == 0;
                    if (!message.SearchComplete)
                    {
                        ushort remainingMs = BitConverter.ToUInt16(bytes, bytes.Length - 2);
                        if (remainingMs == 0 || remainingMs > LootCatalog.SearchItemSeconds * 1000f) return false;
                        message.SearchRemaining = remainingMs * .001f;
                    }
                    message.Items = new LootItem[bytes[2]];
                    message.Unknown = new LootFootprint[bytes[1]];
                    for (int i = 0; i < message.Items.Length; i++)
                    {
                        int offset = 3 + i * 8;
                        var item = new LootItem
                        {
                            Id = BitConverter.ToUInt32(bytes, offset), Type = bytes[offset + 4],
                            X = bytes[offset + 5], Y = bytes[offset + 6], Rotated = bytes[offset + 7] == 1
                        };
                        if (item.Id == 0 || item.Type == 0 || item.Type > LootCatalog.TypeCount || bytes[offset + 7] > 1 ||
                            (message.Kind != RaidEventKind.Result && !LootCatalog.CanPlace(message.Items, item, !crate, item.X, item.Y, item.Rotated)))
                            return false;
                        for (int j = 0; j < i; j++)
                            if (message.Items[j].Id == item.Id) return false;
                        message.Items[i] = item;
                    }
                    int previousAnchor = -1;
                    for (int i = 0; i < message.Unknown.Length; i++)
                    {
                        int offset = 3 + message.Items.Length * 8 + i * 4;
                        var shape = new LootFootprint { X = bytes[offset], Y = bytes[offset + 1],
                            Width = bytes[offset + 2], Height = bytes[offset + 3] };
                        int anchor = shape.Y * LootCatalog.CrateWidth + shape.X;
                        if (anchor <= previousAnchor || shape.Width == 0 || shape.Height == 0 ||
                            shape.X + shape.Width > LootCatalog.CrateWidth || shape.Y + shape.Height > LootCatalog.CrateHeight)
                            return false;
                        foreach (var known in message.Items)
                            if (LootFootprint.Of(known).Overlaps(shape.X, shape.Y, shape.Width, shape.Height)) return false;
                        for (int j = 0; j < i; j++)
                            if (message.Unknown[j].Overlaps(shape.X, shape.Y, shape.Width, shape.Height)) return false;
                        message.Unknown[i] = shape;
                        previousAnchor = anchor;
                    }
                    return true;
                case RaidEventKind.Gear when bytes.Length == 2 + 5 * (GearCatalog.Slots - 1):
                    message.Gear = new LootItem[GearCatalog.Slots];
                    for (int i = 1; i < GearCatalog.Slots; i++)
                    {
                        int offset = 2 + 5 * (i - 1);
                        uint id = BitConverter.ToUInt32(bytes, offset);
                        byte type = bytes[offset + 4];
                        if (id == 0 && type == 0) continue;
                        if (id == 0 || !GearCatalog.Accepts((GearLocation)i, type) ||
                            !GearCatalog.Available((GearLocation)i, message.Gear)) return false;
                        for (int j = 1; j < i; j++) if (message.Gear[j].Id == id) return false;
                        message.Gear[i] = new LootItem { Id = id, Type = type };
                    }
                    message.ActiveGear = (GearLocation)bytes[1];
                    return bytes[1] == 0 || (GearCatalog.Selectable(message.ActiveGear) && message.Gear[bytes[1]].Id != 0);
                case RaidEventKind.Status when bytes.Length == 6:
                    message.Status = (RaidStatus)bytes[1];
                    message.Operation = BitConverter.ToUInt32(bytes, 2);
                    return bytes[1] >= (byte)RaidStatus.Opened && bytes[1] <= (byte)RaidStatus.Stale;
                case RaidEventKind.PouchLevel when bytes.Length == 2:
                    message.PouchLevel = bytes[1];
                    return message.PouchLevel >= 1 && message.PouchLevel <= 4;
                case RaidEventKind.ExitProgress when bytes.Length == 5:
                    message.Seconds = BitConverter.ToSingle(bytes, 1);
                    return Finite(message.Seconds) && message.Seconds >= 0f && message.Seconds <= 3f;
                case RaidEventKind.RaidStarted when bytes.Length == 5:
                case RaidEventKind.RaidQueue when bytes.Length == 6:
                    message.Round = BitConverter.ToUInt32(bytes, 1);
                    if (message.Round == 0 || (bytes.Length == 6 && bytes[5] > 1)) return false;
                    message.Queued = bytes.Length == 6 && bytes[5] == 1;
                    return true;
                default:
                    return false;
            }
        }
    }

    // Enabled only by explicitly supplying an audit path to the process.
    internal sealed class RaidAudit : IDisposable
    {
        [Serializable]
        private sealed class Entry
        {
            public long sequence;
            public string direction;
            public ulong peer;
            public string channel;
            public string kind;
            public string payload;
        }

        private readonly StreamWriter log;
        private long sequence;

        private RaidAudit(string path)
        {
            string fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            log = new StreamWriter(new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.Read),
                new UTF8Encoding(false)) { AutoFlush = true };
        }

        public static RaidAudit Open(string path) => string.IsNullOrWhiteSpace(path) ? null : new RaidAudit(path);

        public void Write(string direction, ulong peer, string name, byte[] bytes)
        {
            bool command = name == RaidWire.Commands;
            var entry = new Entry
            {
                sequence = ++sequence,
                direction = direction,
                peer = peer,
                channel = command ? "command" : "event",
                kind = command ? ((RaidCommand)bytes[0]).ToString() : ((RaidEventKind)bytes[0]).ToString(),
                payload = Convert.ToBase64String(bytes)
            };
            log.WriteLine(JsonUtility.ToJson(entry));
        }

        public void Dispose() => log.Dispose();
    }

    public sealed class RaidClientLink : IDisposable
    {
        public event Action<RaidEvent> Received;
        private uint transferSequence;
        private readonly NetworkManager net;
        private readonly RaidAudit audit;

        public RaidClientLink(NetworkManager net, string auditPath = null)
        {
            this.net = net;
            audit = RaidAudit.Open(auditPath);
            net.CustomMessagingManager.RegisterNamedMessageHandler(RaidWire.Events, OnEvent);
        }

        private void OnEvent(ulong sender, FastBufferReader reader)
        {
            if (sender != NetworkManager.ServerClientId)
                return;
            byte[] bytes = RaidWire.Read(reader);
            if (bytes == null)
                return;
            audit?.Write("rx", sender, RaidWire.Events, bytes);
            if (!RaidWire.TryDecode(bytes, out var message)) return;
            if (message.Kind == RaidEventKind.RaidStarted)
            {
                var accepted = new byte[5];
                accepted[0] = (byte)RaidCommand.EnterRaid;
                Buffer.BlockCopy(BitConverter.GetBytes(message.Round), 0, accepted, 1, 4);
                Send(accepted);
            }
            Received?.Invoke(message);
        }

        public void SetNextRaidReady(uint completedRound, bool ready)
        {
            if (completedRound == 0) return;
            var bytes = new byte[6];
            bytes[0] = (byte)RaidCommand.NextRaid;
            Buffer.BlockCopy(BitConverter.GetBytes(completedRound), 0, bytes, 1, 4);
            bytes[5] = ready ? (byte)1 : (byte)0;
            Send(bytes);
        }

        public void Move(Vector2 worldDirection, float yaw, float pitch, RaidCameraMode mode, float aspect, byte shoulderSample)
            => Move(worldDirection, yaw, pitch, mode, aspect, shoulderSample, MoveButtons.None);

        internal void Move(Vector2 worldDirection, float yaw, float pitch, RaidCameraMode mode, float aspect,
            byte shoulderSample, MoveButtons buttons)
        {
            var bytes = new byte[24];
            bytes[0] = (byte)RaidCommand.Move;
            RaidWire.Put(bytes, 1, worldDirection.x);
            RaidWire.Put(bytes, 5, worldDirection.y);
            RaidWire.Put(bytes, 9, yaw);
            RaidWire.Put(bytes, 13, pitch);
            bytes[17] = (byte)mode;
            RaidWire.Put(bytes, 18, aspect);
            bytes[22] = shoulderSample;
            bytes[23] = (byte)buttons;
            Send(bytes);
        }

        public void Open() => Send(new[] { (byte)RaidCommand.Open });
        public void SetWarehouseDoor(bool open) =>
            Send(new[] { (byte)RaidCommand.SetWarehouseDoor, (byte)(open ? 1 : 0) });
        public void Close() => Send(new[] { (byte)RaidCommand.Close });
        public void EnterBase() => Send(new[] { (byte)RaidCommand.EnterBase });
        public void SetPouchLevel(byte level)
        {
            if (level >= 1 && level <= 4) Send(new[] { (byte)RaidCommand.SetPouchLevel, level });
        }
        public uint Take(uint id) => Transfer(RaidCommand.Take, id);
        public uint Store(uint id) => Transfer(RaidCommand.Store, id);

        public uint Place(LootItem item, bool fromBag, bool toBag, int x, int y, bool rotated)
        {
            if (net == null || !net.IsConnectedClient || item.Id == 0 || transferSequence == uint.MaxValue ||
                x < 0 || x > 255 || y < 0 || y > 255) return 0;
            var bytes = new byte[14];
            bytes[0] = (byte)RaidCommand.Place;
            Buffer.BlockCopy(BitConverter.GetBytes(++transferSequence), 0, bytes, 1, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(item.Id), 0, bytes, 5, 4);
            bytes[9] = (byte)((fromBag ? 1 : 0) | (toBag ? 2 : 0) | (rotated ? 4 : 0) | (item.Rotated ? 8 : 0));
            bytes[10] = (byte)x;
            bytes[11] = (byte)y;
            bytes[12] = item.X;
            bytes[13] = item.Y;
            Send(bytes);
            return transferSequence;
        }
        public uint MoveGear(uint id, GearLocation from, GearLocation to, int x = -1, int y = -1, bool rotated = false)
        {
            if (net == null || !net.IsConnectedClient || transferSequence == uint.MaxValue || id == 0 ||
                (x == -1) != (y == -1) || x < -1 || x > 254 || y < -1 || y > 254) return 0;
            var bytes = new byte[14];
            bytes[0] = (byte)RaidCommand.MoveGear;
            Buffer.BlockCopy(BitConverter.GetBytes(++transferSequence), 0, bytes, 1, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(id), 0, bytes, 5, 4);
            bytes[9] = (byte)from; bytes[10] = (byte)to;
            bytes[11] = (byte)(x < 0 ? 255 : x); bytes[12] = (byte)(y < 0 ? 255 : y);
            bytes[13] = (byte)(rotated ? 1 : 0);
            Send(bytes);
            return transferSequence;
        }

        public uint SelectGear(GearLocation slot)
        {
            if (net == null || !net.IsConnectedClient || transferSequence == uint.MaxValue || !GearCatalog.Selectable(slot))
                return 0;
            var bytes = new byte[6];
            bytes[0] = (byte)RaidCommand.SelectGear;
            Buffer.BlockCopy(BitConverter.GetBytes(++transferSequence), 0, bytes, 1, 4);
            bytes[5] = (byte)slot;
            Send(bytes);
            return transferSequence;
        }

        private uint Transfer(RaidCommand direction, uint id)
        {
            if (net == null || !net.IsConnectedClient || id == 0 || transferSequence == uint.MaxValue)
                return 0;
            var bytes = new byte[9];
            bytes[0] = (byte)direction;
            Buffer.BlockCopy(BitConverter.GetBytes(++transferSequence), 0, bytes, 1, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(id), 0, bytes, 5, 4);
            Send(bytes);
            return transferSequence;
        }

        private void Send(byte[] bytes)
        {
            if (net != null && net.IsConnectedClient)
                RaidWire.Send(net, NetworkManager.ServerClientId, RaidWire.Commands, bytes, audit);
        }

        public void Dispose()
        {
            if (net != null)
                net.CustomMessagingManager?.UnregisterNamedMessageHandler(RaidWire.Events);
            audit?.Dispose();
            Received = null;
        }
    }
}
