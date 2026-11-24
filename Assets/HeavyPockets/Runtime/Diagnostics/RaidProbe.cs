#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace HeavyPockets
{
    // Test driver for a real client. The shared directory coordinates the two drivers
    // only. Every game action and observation goes through the normal network link.
    public sealed partial class RaidProbe : MonoBehaviour
    {
        private NetworkManager network;
        private RaidClientLink link;
        private string role, directory, phase;
        private float phaseStarted;
        private bool captureFrames;
        private Vector2 position;
        private bool havePose, blocked, gotTaken, gotEmpty, finished, searchComplete;
        private int crateFrames, closes, noSpace;
        private bool wonRace;
        private RaidClient Client => GetComponent<RaidClient>();
        private LootItem[] crate = Array.Empty<LootItem>(), bag = Array.Empty<LootItem>(), result;
        private LootItem[] completeCrate;
        private int remotePoses, remoteHides;
        private RaidLocomotion remoteMotion;
        private bool waitForMovingPose;
        private Vector2 firstMovingPose;
        private uint watchedDrop;
        private bool watchedBag, watchedRotation;
        private int watchedX, watchedY, dropResponses;
        private double enteredAt = -1;
        private double observedHold;

        public void Initialize(NetworkManager net, RaidClientLink connection, string probeRole, string probeDir)
        {
            network = net;
            link = connection;
            role = probeRole;
            captureFrames = Array.Exists(Environment.GetCommandLineArgs(), arg => arg == "-probeCapture");
            directory = probeDir;
            Directory.CreateDirectory(directory);
            Application.runInBackground = true;
            link.Received += Observe;
            StartCoroutine(Run());
        }

        private void Observe(RaidEvent message)
        {
            if (watchedDrop != 0 && (message.Kind == RaidEventKind.Status || message.Kind == RaidEventKind.Bag || message.Kind == RaidEventKind.Crate))
            {
                if (!Client.ProbeVisiblePlacement(watchedDrop, watchedBag, watchedX, watchedY, watchedRotation))
                {
                    Fail("A response or partial inventory snapshot put the dropped item back at its source");
                    return;
                }
                dropResponses++;
            }
            switch (message.Kind)
            {
                case RaidEventKind.Gear:
                    gearSlots = message.Gear;
                    activeSlot = message.ActiveGear;
                    break;
                case RaidEventKind.RaidStarted:
                    gearSlots = Array.Empty<LootItem>();
                    havePose = searchComplete = freshBag = false;
                    crateFrames = 0;
                    crate = bag = Array.Empty<LootItem>();
                    result = null;
                    enteredAt = -1;
                    break;
                case RaidEventKind.Pose:
                    if (message.Actor == network.LocalClientId)
                    {
                        havePose = true;
                        position = message.Position;
                        if (!RaidMap.InExit(position))
                            enteredAt = -1;
                        else if (enteredAt < 0)
                            enteredAt = Time.realtimeSinceStartupAsDouble;
                    }
                    else
                    {
                        remotePoses++;
                        remoteMotion = message.Motion;
                        if (waitForMovingPose)
                        {
                            firstMovingPose = message.Position;
                            waitForMovingPose = false;
                        }
                    }
                    break;
                case RaidEventKind.Left:
                    if (message.Actor != network.LocalClientId)
                        remoteHides++;
                    break;
                case RaidEventKind.Status:
                    blocked |= message.Status == RaidStatus.Blocked;
                    gotTaken |= message.Status == RaidStatus.Taken;
                    gotEmpty |= message.Status == RaidStatus.Empty;
                    if (message.Status == RaidStatus.Closed) closes++;
                    if (message.Status == RaidStatus.NoSpace) noSpace++;
                    if (message.Status == RaidStatus.Denied) gearDenials++;
                    break;
                case RaidEventKind.Crate:
                    crateFrames++;
                    crate = message.Items;
                    searchComplete = message.SearchComplete;
                    break;
                case RaidEventKind.Bag:
                    bag = message.Items;
                    freshBag = true;
                    break;
                case RaidEventKind.Result:
                    result = message.Items;
                    observedHold = enteredAt < 0 ? 0 : Time.realtimeSinceStartupAsDouble - enteredAt;
                    break;
            }
        }

        private void Phase(string name)
        {
            phase = name;
            phaseStarted = Time.realtimeSinceStartup;
        }

        private void Update()
        {
            if (link != null && !finished && Time.realtimeSinceStartup - phaseStarted > 80f)
                Fail("Timeout in " + phase + " at " + position);
        }

        private void OnDestroy()
        {
            if (link != null) link.Received -= Observe;
        }

        private void Signal(string marker, string value = "ok")
        {
            // Publish complete markers, including the shared UTC deadline, atomically.
            string path = Path.Combine(directory, role + "." + marker);
            File.WriteAllText(path + ".tmp", value);
            File.Move(path + ".tmp", path);
        }

        private bool Exists(string marker) => File.Exists(Path.Combine(directory, marker));

        private string Marker(string marker) => File.ReadAllText(Path.Combine(directory, marker)).Trim();

        private static string Contents(LootItem[] items) =>
            string.Join(",", items.OrderBy(item => item.Id).Select(item => item.Id + "/" + item.Type));

        private void Capture(string name)
        {
#if !UNITY_SERVER
            if (captureFrames && !Application.isBatchMode && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                ScreenCapture.CaptureScreenshot(Path.Combine(directory, role + "-" + name + ".png"));
#endif
        }

        private void Fail(string reason)
        {
            if (finished) return;
            finished = true;
            Debug.LogError("[probe " + role + "] " + reason);
            Signal("failed", reason);
            StopAllCoroutines();
            Application.Quit(2);
        }

        private IEnumerator Walk(Vector2 target, float? heading = null, float elevation = 3f, float maxInput = 1f,
            bool sprint = false)
        {
            Phase("walk to " + target);
            float finalYaw = heading ?? 0f;
            float tolerance = heading.HasValue ? 0.012f : 0.24f;
            while (Vector2.Distance(position, target) > tolerance)
            {
                Vector2 delta = target - position;
                Vector2 direction = Vector2.ClampMagnitude(delta, maxInput);
                finalYaw = heading ?? Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg;
                Client.SetProbeMotion(direction, finalYaw, elevation, sprint && delta.sqrMagnitude > 4f
                    ? MoveButtons.Sprint : MoveButtons.None);
                yield return new WaitForSecondsRealtime(0.08f);
            }
            Client.SetProbeMotion(Vector2.zero, finalYaw, elevation);
            yield return new WaitForSecondsRealtime(0.24f);
        }

        private IEnumerator CheckPartial(string name)
        {
            if (!captureFrames) yield break;
            yield return new WaitForEndOfFrame();
            Vector3 eye = Camera.main.transform.position;
            if (RaidMap.HasSightLine(new Vector2(eye.x, eye.z), RaidMap.Spawn(1)))
            {
                Fail("Runner center already clear in " + name + " from " + eye);
                yield break;
            }
            bool rendered = Client.ProbeRemoteSilhouette(out int exposed, out int silhouette);
            if (!rendered || exposed < 2 || exposed >= 0.85f * silhouette)
            {
                Fail("Expected a visible slice of the runner in " + name + ", pixels " + exposed + "/" + silhouette + " from " + eye);
                yield break;
            }
            Signal(name, exposed + "/" + silhouette);
            Capture(name);
            yield return new WaitForSecondsRealtime(0.16f);
        }

        private IEnumerator CheckVisibility()
        {
            if (remotePoses != 0 || remoteHides != 0)
            {
                Fail("Received a remote actor while both spawns were behind the partition");
                yield break;
            }
            if (captureFrames)
            {
                var eye = new Vector3(position.x, 1.58f, position.y);
                Client.LookAtForCapture(eye + Vector3.back * 2f);
                yield return new WaitForSecondsRealtime(0.35f);
                Vector3 camera = Camera.main.transform.position;
                float retracted = Vector3.Distance(camera, eye);
                if (camera.z >= -RaidMap.WallZ || retracted >= 1.0f)
                {
                    Fail("The shoulder camera crossed the partition behind the runner");
                    yield break;
                }
                Signal("camera-wall", retracted.ToString("F2", CultureInfo.InvariantCulture));
            }
            // The first-person camera is completely behind the partition here.
            // Switching into the shoulder view does not move either player's body.
            Client.TogglePerspective();
            // The Armature first appears before its centre clears the partition at
            // these positions, both in the 16:10 capture and at 16:9 headless.
            yield return Walk(new Vector2(1.83f, -1f), -33f, 5f);
            if (remotePoses != 0) { Fail("First-person camera received the hidden runner"); yield break; }
            Client.TogglePerspective();
            Client.SetProbeMotion(Vector2.zero, -33f, 5f);
            Phase("stationary third-person corner peek");
            yield return new WaitUntil(() => remotePoses > 0);
            yield return CheckPartial("third-corner");

            Vector2 peekPosition = position;
            Client.ToggleShoulder();
            Client.SetProbeMotion(Vector2.zero, -33f, 5.0625f);
            Phase("left shoulder behind cover");
            yield return new WaitUntil(() => remoteHides > 0);
            int before = remotePoses;
            yield return new WaitForSecondsRealtime(0.40f);
            yield return new WaitUntil(() => Client.ProbeShoulderSample == 0);
            Client.SetProbeMotion(Vector2.zero, -33f, 5.0625f);
            if (!Client.IsLeftShoulder || remotePoses != before || remoteHides != 1)
            {
                Fail("Left shoulder kept receiving poses through cover");
                yield break;
            }
            Client.ToggleShoulder();
            Client.SetProbeMotion(Vector2.zero, -33f, 5.09375f);
            Phase("right shoulder regains partial sight");
            yield return new WaitUntil(() => remotePoses > before);
            yield return new WaitUntil(() => Client.ProbeShoulderSample == RaidCameraBoom.LastSample);
            Client.SetProbeMotion(Vector2.zero, -33f, 5.09375f);
            if (Client.IsLeftShoulder || (position - peekPosition).sqrMagnitude > .0001f)
            {
                Fail("Shoulder switch moved the player or did not restore the right shoulder");
                yield break;
            }
            Signal("shoulder-switch", "Stationary right-left-right: one hide and zero remote poses during the covered interval.");
            Client.TogglePerspective();
            Client.SetProbeMotion(Vector2.zero, -33f, 5.125f);
            Phase("fully occluded again in first person");
            yield return new WaitUntil(() => remoteHides >= 2);
            before = remotePoses;
            yield return new WaitForSecondsRealtime(0.55f);
            if (remotePoses != before || remoteHides != 2)
            {
                Fail("Remote updates continued behind the partition");
                yield break;
            }
            yield return Walk(new Vector2(2.80f, -1f), -33f, 5.25f);
            Phase("first-person corner peek");
            yield return new WaitUntil(() => remotePoses > before);
            yield return CheckPartial("first-corner");
            if (captureFrames)
            {
                if (!Client.ProbeRemoteReveal(126f, out string evidence))
                {
                    Fail(evidence);
                    yield break;
                }
                Signal("remote-reveal", evidence);
            }
            // Same observer and target positions, with only the viewing direction reversed.
            Client.SetProbeMotion(Vector2.zero, 147f, 6.25f);
            Phase("outside the first-person field of view");
            yield return new WaitUntil(() => remoteHides == 3);
            before = remotePoses;
            yield return new WaitForSecondsRealtime(0.35f);
            if (before != remotePoses) { Fail("Actor behind the camera kept sending poses"); yield break; }
            Client.SetProbeMotion(Vector2.zero, -33f, 5.375f);
            Phase("back in view after turning");
            yield return new WaitUntil(() => remotePoses > before);
            yield return Walk(new Vector2(1.90f, -1f), 0f, 4.625f);
            Phase("stationary observer watching moving target");
            yield return new WaitUntil(() => remoteHides >= 4);
            int hidesBeforePass = remoteHides;
            waitForMovingPose = true;
            Signal("moving-peek");
            yield return new WaitUntil(() => !waitForMovingPose);
            if (RaidMap.HasSightLine(position, firstMovingPose))
            {
                Fail("Moving runner did not appear until its center cleared the wall");
                yield break;
            }
            yield return new WaitUntil(() => remoteHides > hidesBeforePass);
            int hiddenCount = remotePoses;
            yield return new WaitUntil(() => Exists("B.peek-returned"));
            if (hiddenCount != remotePoses) { Fail("Actor movements were received after returning to cover"); yield break; }
            Client.SetProbeMotion(Vector2.zero, 0f, 4.875f);
            Client.TogglePerspective();
            yield return Walk(new Vector2(2.95f, -1f));
            yield return Walk(new Vector2(2.95f, 1.7f));
        }

        private IEnumerator CapturePerspectives()
        {
            var client = GetComponent<RaidClient>();
            Camera camera = Camera.main;
            Vector2 other = RaidMap.Spawn(1);
            client.LookAtForCapture(new Vector3(other.x - 0.37f, 1.31f, other.y + 0.19f));
            yield return new WaitForSecondsRealtime(0.25f);
            yield return new WaitForEndOfFrame();
            Vector3 eye = new Vector3(position.x, 1.58f, position.y);
            Vector3 chest = eye + Vector3.down * 0.55f;
            if (camera == null || !client.IsThirdPerson || !client.OwnAvatarVisible)
            {
                Fail("Default third-person camera or the runner model is missing");
                yield break;
            }
            float third = Vector3.Distance(camera.transform.position, eye);
            Vector3 viewport = camera.WorldToViewportPoint(chest);
            if (third < 0.8f || client.IsLeftShoulder || viewport.z <= 0f || viewport.x <= 0f || viewport.x >= .44f || viewport.y <= 0f || viewport.y >= 1f)
            {
                Fail("Third-person camera does not frame the local runner at the terminal");
                yield break;
            }
            Capture("terminal");
            yield return new WaitForSecondsRealtime(0.25f);
            Capture("right-shoulder");
            yield return new WaitForSecondsRealtime(0.16f);
            Quaternion aim = camera.transform.rotation;
            Vector3 rightOrigin = camera.transform.position;
            client.ToggleShoulder();
            if ((camera.transform.position - rightOrigin).sqrMagnitude > .000001f)
            { Fail("Pressing X teleported the camera"); yield break; }
            yield return new WaitUntil(() => client.ProbeShoulderSample < 190);
            yield return new WaitForEndOfFrame();
            Vector3 turnOrigin = camera.transform.position;
            float turnFraction = camera.WorldToViewportPoint(chest).x;
            client.ToggleShoulder();
            if ((camera.transform.position - turnOrigin).sqrMagnitude > .000001f ||
                turnFraction <= viewport.x + .01f || turnFraction >= .62f)
            { Fail("Reversing X skipped the intermediate shoulder position"); yield break; }
            yield return new WaitUntil(() => client.ProbeShoulderSample == RaidCameraBoom.LastSample);
            yield return new WaitForEndOfFrame();
            if ((camera.transform.position - rightOrigin).magnitude > .035f)
            { Fail("Reversing the shoulder did not return to the same camera"); yield break; }
            client.ToggleShoulder();
            int interiorFrames = 0;
            float previousX = viewport.x, largestStep = 0f;
            do
            {
                yield return new WaitForEndOfFrame();
                float currentX = camera.WorldToViewportPoint(chest).x;
                largestStep = Mathf.Max(largestStep, Mathf.Abs(currentX - previousX));
                if (currentX < previousX - .003f || Quaternion.Angle(camera.transform.rotation, aim) > .05f)
                { Fail("The shoulder path reversed direction or changed the aim"); yield break; }
                if (client.ProbeShoulderSample > 0 && client.ProbeShoulderSample < RaidCameraBoom.LastSample)
                    interiorFrames++;
                previousX = currentX;
            } while (client.ProbeShoulderSample != 0);
            Vector3 left = camera.WorldToViewportPoint(chest);
            if (!client.IsLeftShoulder || !client.OwnAvatarVisible || left.z <= 0f || left.x <= .56f || left.x >= 1f ||
                left.y <= 0f || left.y >= 1f || Quaternion.Angle(camera.transform.rotation, aim) > .05f)
            {
                Fail("Left shoulder failed to frame the runner on the opposite side with a stable aim");
                yield break;
            }
            if (interiorFrames < 2 || largestStep > (left.x - viewport.x) * .45f)
            { Fail("Shoulder transition omitted its intermediate rendered frames"); yield break; }
            Signal("shoulder-smooth", string.Format(CultureInfo.InvariantCulture,
                "{0} intermediate frames; largest screen-X step {1:F4}; mid-flight reversal at {2:F4}",
                interiorFrames, largestStep, turnFraction));
            Capture("left-shoulder");
            yield return new WaitForSecondsRealtime(0.25f);
            client.TogglePerspective();
            yield return new WaitForSecondsRealtime(0.25f);
            yield return new WaitForEndOfFrame();
            float first = Vector3.Distance(camera.transform.position, eye);
            if (client.IsThirdPerson || !client.IsLeftShoulder || client.OwnAvatarVisible || first > 0.15f)
            {
                Fail("First-person camera did not reach eye level or hide the local model");
                yield break;
            }
            Capture("first-person");
            yield return new WaitForSecondsRealtime(0.25f);
            client.TogglePerspective();
            yield return new WaitForSecondsRealtime(0.25f);
            if (!client.IsThirdPerson || !client.IsLeftShoulder || !client.OwnAvatarVisible ||
                Vector3.Distance(camera.transform.position, eye) < 0.8f || camera.WorldToViewportPoint(chest).x <= .56f)
            {
                Fail("Third-person camera failed to restore");
                yield break;
            }
            client.ToggleShoulder();
            Signal("perspective", string.Format(CultureInfo.InvariantCulture,
                "third {0:F2} m, first {1:F2} m, chest X {2:P1} right / {3:P1} left. V restored the left shoulder.",
                third, first, viewport.x, left.x));
        }

        private static bool ContainsRect(Rect outer, Rect inner) =>
            inner.width > 0 && inner.height > 0 && inner.xMin >= outer.xMin - .01f && inner.yMin >= outer.yMin - .01f
            && inner.xMax <= outer.xMax + .01f && inner.yMax <= outer.yMax + .01f;

        private static bool DockFits(bool open, Vector2 size)
        {
            RaidHud.ProbePanelsAt(open, size, out Rect right, out Rect center, out Rect bagGrid, out Rect crateGrid, out var slots);
            var screen = new Rect(Vector2.zero, size);
            if (!ContainsRect(screen, right) || right.xMin < .45f * size.x || !ContainsRect(right, bagGrid)) return false;
            for (int i = 0; i < slots.Length; i++)
            {
                if (!ContainsRect(right, slots[i]) || slots[i].Overlaps(bagGrid)) return false;
                for (int j = 0; j < i; j++) if (slots[i].Overlaps(slots[j])) return false;
            }
            if (!open) return center.width == 0 && crateGrid.width == 0;
            return ContainsRect(screen, center) && ContainsRect(center, crateGrid) && center.xMax <= right.xMin
                && center.xMin >= .25f * size.x;
        }

        private static bool IllustrationFits(Rect grid, LootItem item)
        {
            var origin = new Vector2(item.X, item.Y);
            var size = new Vector2(LootCatalog.Width(item.Type, item.Rotated), LootCatalog.Height(item.Type, item.Rotated));
            var footprint = new Rect(grid.position + origin * RaidHud.Cell, size * RaidHud.Cell);
            return ContainsRect(grid, footprint) && ContainsRect(footprint, RaidHud.ProbeInventoryArt(grid, item));
        }

        private bool CheckDockImages()
        {
            RaidHud.ProbeInventoryLayout(true, out _, out Rect pack, out Rect chest, out float scale);
            foreach (var size in new[] { new Vector2(Screen.width, Screen.height) / scale,
                new Vector2(840, 630), new Vector2(1167, 500) })
                if (!DockFits(true, size) || !DockFits(false, size)) return false;
            foreach (var item in crate)
            {
                if (!IllustrationFits(chest, item)) return false;
                var test = item;
                for (int turn = 0; turn < 2; turn++)
                {
                    // Place both orientations flush against the far edge of the smaller grid.
                    test.Rotated = turn != 0;
                    test.X = (byte)(LootCatalog.BagWidth - LootCatalog.Width(test.Type, test.Rotated));
                    test.Y = (byte)(LootCatalog.BagHeight - LootCatalog.Height(test.Type, test.Rotated));
                    if (!IllustrationFits(pack, test)) return false;
                }
            }
            return true;
        }

        private static float[] ReadLeftWorld()
        {
#if UNITY_SERVER
            return null;
#else
            // Sample the central height band outside the play HUD and the right dock.
            // Regional means tolerate sub-pixel changes in the settling shoulder camera.
            Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                var pixels = frame.GetPixels32();
                var mean = new float[24];
                for (int block = 0; block < mean.Length; block++)
                {
                    int col = block % 6, row = block / 6;
                    int count = 0;
                    for (int y = (20 + 15 * row) * frame.height / 100; y < (35 + 15 * row) * frame.height / 100; y += 7)
                        for (int x = (1 + 4 * col) * frame.width / 100; x < (5 + 4 * col) * frame.width / 100; x += 7)
                        {
                            Color32 c = pixels[y * frame.width + x];
                            mean[block] += .213f * c.r + .715f * c.g + .072f * c.b;
                            count++;
                        }
                    mean[block] /= Mathf.Max(1, count);
                }
                return mean;
            }
            finally { Destroy(frame); }
#endif
        }

        private IEnumerator Search()
        {
            Phase("private search");
            bool sampleDock = role == "A" && captureFrames && !Application.isBatchMode
                && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
            float[] baseline = null;
            if (sampleDock)
            {
                yield return new WaitForEndOfFrame();
                baseline = ReadLeftWorld();
            }
            double started = Time.realtimeSinceStartupAsDouble;
            link.Open();
            if (role == "A")
            {
                yield return new WaitUntil(() => crateFrames > 0);
                if (crate.Length != 0 || searchComplete) { Fail("Initial search exposed an identified item"); yield break; }
                Capture("footprints");
                yield return new WaitUntil(() => crate.Length >= 2);
                Capture("search");
                if (sampleDock)
                {
                    yield return new WaitForEndOfFrame();
                    float[] opened = ReadLeftWorld();
                    float drift = 0;
                    for (int i = 0; i < opened.Length; i++) drift += Mathf.Abs(opened[i] - baseline[i]);
                    drift /= opened.Length;
                    if (drift > 7 || !CheckDockImages())
                    {
                        Fail("Search dock covered the left scene or clipped an image. Left luminance delta " + drift);
                        yield break;
                    }
                    Signal("dock", string.Format(CultureInfo.InvariantCulture,
                        "left world luminance delta {0:F2}/255 across 24 regions, both panels and rotated atlas images fit", drift));
                }
                yield return new WaitForSecondsRealtime(0.20f);
                link.Close();
                yield return new WaitUntil(() => closes == 1);
                int pausedFrames = crateFrames;
                string remembered = Contents(crate);
                yield return new WaitForSecondsRealtime(1.45f);
                if (sampleDock)
                {
                    var png = new FileInfo(Path.Combine(directory, role + "-search.png"));
                    if (!png.Exists || png.Length == 0) { Fail("Missing search capture"); yield break; }
                }
                if (crateFrames != pausedFrames || searchComplete)
                {
                    Fail("Search disclosed contents while closed or revealed everything immediately");
                    yield break;
                }
                link.Open();
                yield return new WaitUntil(() => crateFrames > pausedFrames);
                if (Contents(crate) != remembered)
                {
                    Fail("Reopening lost discoveries or advanced the search while closed");
                    yield break;
                }
            }
            yield return new WaitUntil(() => searchComplete);
            completeCrate = crate;
            Signal("search-ms", ((Time.realtimeSinceStartupAsDouble - started) * 1000).ToString("F0", CultureInfo.InvariantCulture));
            Signal("opened", Contents(completeCrate));
            Capture("crate");
            if (sampleDock)
            {
                yield return new WaitForSecondsRealtime(.18f);
                var inspected = completeCrate[0];
                Client.ProbeHover(false, inspected.X + .5f, inspected.Y + .5f);
                yield return new WaitUntil(() => Client.ProbeTipVisible);
                Capture("item-card");
                yield return new WaitForSecondsRealtime(.18f);
            }
        }

        private IEnumerator CaptureEquipment()
        {
            if (role != "A" || !captureFrames) yield break;
            bool openedHere = !Client.ProbeInventoryOpen;
            if (openedHere) Client.ToggleInventory();
            Client.ProbeHover(true, -.5f, .5f);
            yield return new WaitForSecondsRealtime(.2f);
            Capture("equipment");
            yield return new WaitForSecondsRealtime(.2f);
            if (Client.ProbeGearItem(GearLocation.Armor).Id == 0)
            { Fail("Equipment screenshot was missing its restored armor"); yield break; }
            Client.ProbeGearHover(GearLocation.Armor);
            yield return new WaitUntil(() => Client.ProbeTipVisible);
            Capture("equipment-card");
            yield return new WaitForSecondsRealtime(.18f);
            if (openedHere) Client.ToggleInventory();
        }

        private static string Layout(LootItem[] items) => string.Join(",", items.OrderBy(i => i.Id)
            .Select(i => $"{i.Id}/{i.Type}@{i.X},{i.Y},{(i.Rotated ? 1 : 0)}"));

        private static bool At(LootItem[] items, uint id, int x, int y, bool rotated) =>
            items.Any(item => item.Id == id && item.X == x && item.Y == y && item.Rotated == rotated);

        private IEnumerator Gesture(LootItem item, bool fromBag, bool toBag, int x, int y,
            bool rotated, bool valid, string capture = null)
        {
            var client = Client;
            client.ProbePointer(fromBag, item.X + 0.19f, item.Y + 0.78f, EventType.MouseDown);
            client.ProbePointer(fromBag, item.X + 0.31f, item.Y + 0.69f, EventType.MouseDrag);
            Vector2 firstCenter = client.ProbeGhostCenter;
            client.ProbePointer(fromBag, item.X + 0.56f, item.Y + 0.56f, EventType.MouseDrag);
            Vector2 beforeTurn = client.ProbeGhostCenter;
            var expectedMotion = RaidHud.Cell * new Vector2(0.25f, -0.13f);
            if (Vector2.Distance(beforeTurn - firstCenter, expectedMotion) > 0.05f)
            {
                Fail("Item did not follow sub-cell pointer motion");
                yield break;
            }
            if (rotated != item.Rotated && !client.ProbeRotateHeld())
            {
                Fail("Could not rotate the held grid item");
                yield break;
            }
            if (Vector2.Distance(beforeTurn, client.ProbeGhostCenter) > 0.05f)
            {
                Fail("Rotation shifted the center of the held item");
                yield break;
            }
            client.ProbeTargetCell(toBag, x, y, EventType.MouseDrag);
            if (client.ProbeValidTarget(toBag, x, y) != valid)
            {
                Fail("Incorrect drop preview at " + x + "," + y);
                yield break;
            }
            if (capture != null && captureFrames)
            {
                yield return new WaitForSecondsRealtime(0.20f);
                Capture(capture);
                yield return new WaitForSecondsRealtime(0.25f);
            }
            client.ProbeTargetCell(toBag, x, y, EventType.MouseUp);
            if (valid)
            {
                if (!client.ProbePendingPlacement(item.Id) || !client.ProbeVisiblePlacement(item.Id, toBag, x, y, rotated))
                {
                    Fail("Drop target was not displayed immediately on mouse release");
                    yield break;
                }
                watchedDrop = item.Id;
                watchedBag = toBag;
                watchedX = x;
                watchedY = y;
                watchedRotation = rotated;
                dropResponses = 0;
                yield return new WaitUntil(() => At(toBag ? bag : crate, item.Id, x, y, rotated) &&
                    (fromBag == toBag || !(fromBag ? bag : crate).Any(i => i.Id == item.Id)) &&
                    !client.ProbePendingPlacement(item.Id));
                watchedDrop = 0;
                if (dropResponses < (fromBag == toBag ? 2 : 3))
                {
                    Fail("Accepted drop did not receive its expected status and inventory snapshots");
                    yield break;
                }
            }
            yield return new WaitForSecondsRealtime(0.20f);
        }

        private void Quick(bool fromBag, LootItem item)
        {
            Client.ProbeCellInput(fromBag, item.X, item.Y, EventType.MouseDown, true);
            Client.ProbeCellInput(fromBag, item.X, item.Y, EventType.MouseUp, true);
        }

        private IEnumerator TestGrid()
        {
            Phase("grid gestures");
            if (role == "A")
            {
                var original = completeCrate.First(i => LootCatalog.Width(i.Type, false) != LootCatalog.Height(i.Type, false));
                string initial = Layout(completeCrate);
                yield return Gesture(original, false, true, 0, 0, !original.Rotated, true, "drag");
                var item = bag.Single();
                yield return Gesture(item, true, true, 0, 0, original.Rotated, true);
                int x = LootCatalog.BagWidth - LootCatalog.Width(item.Type, original.Rotated);
                int y = GearCatalog.BagRows(gearSlots) - LootCatalog.Height(item.Type, original.Rotated);
                yield return Gesture(bag.Single(), true, true, x, y, original.Rotated, true);
                item = bag.Single();
                var occupied = crate.First();
                string before = Layout(bag) + "|" + Layout(crate);
                yield return Gesture(item, true, false, occupied.X, occupied.Y, item.Rotated, false);
                if (before != Layout(bag) + "|" + Layout(crate)) { Fail("An invalid preview changed ownership"); yield break; }
                // Validate stale/blocked ordinary UI intents independently at the authoritative boundary.
                int denials = noSpace;
                link.Place(item, true, false, occupied.X, occupied.Y, item.Rotated);
                yield return new WaitUntil(() => noSpace > denials);
                link.Place(item, true, true, LootCatalog.BagWidth - 1, LootCatalog.BagHeight - 1, item.Rotated);
                yield return new WaitUntil(() => noSpace >= denials + 2);
                if (before != Layout(bag) + "|" + Layout(crate)) { Fail("Rejected placement changed the grid"); yield break; }
                yield return Gesture(item, true, false, original.X, original.Y, original.Rotated, true);
                if (bag.Length != 0 || Layout(crate) != initial) { Fail("Grid round trip lost an item placement"); yield break; }
                Signal("grid");
            }
            else
                yield return new WaitUntil(() => Exists("A.grid") && Layout(crate) == Layout(completeCrate));
        }

        private IEnumerator TestPersonalGrid()
        {
            yield return Walk(new Vector2(3.55f, 2.45f));
            Phase("personal grid away from the closed crate");
            if (RaidMap.CanOpen(position)) { Fail("Bag check is still within reach of the crate"); yield break; }
            Client.ToggleInventory();
            yield return new WaitForSecondsRealtime(0.25f);
            if (!wonRace)
            {
                var item = bag.Single();
                int x = LootCatalog.BagWidth - LootCatalog.Width(item.Type, item.Rotated);
                int y = GearCatalog.BagRows(gearSlots) - LootCatalog.Height(item.Type, item.Rotated);
                if (x == item.X && y == item.Y) { x = 0; y = 0; }
                yield return Gesture(item, true, true, x, y, item.Rotated, true);
                Signal("personal-move");
            }
            Capture("bag");
            yield return new WaitForSecondsRealtime(0.25f);
            Client.ToggleInventory();
        }

        private IEnumerator Trade()
        {
            Phase("synchronize take commands");
            yield return new WaitUntil(() => Exists("A.opened") && Exists("B.opened"));
            if (completeCrate.Length < 3 || Marker("A.opened") != Marker("B.opened"))
            {
                Fail("Different final searches or insufficient items for a transfer");
                yield break;
            }
            yield return TestGrid();
            gotTaken = gotEmpty = false;
            Phase("synchronize take commands");
            uint contested = completeCrate[0].Id;
            if (role == "A")
                Signal("race", DateTime.UtcNow.AddSeconds(1.2).Ticks.ToString(CultureInfo.InvariantCulture));
            yield return new WaitUntil(() => Exists("A.race"));
            long deadline = long.Parse(Marker("A.race"), CultureInfo.InvariantCulture);
            yield return new WaitUntil(() => DateTime.UtcNow.Ticks >= deadline);
            long actual = DateTime.UtcNow.Ticks;
            link.Take(contested);
            Signal("take", actual.ToString(CultureInfo.InvariantCulture));
            yield return new WaitUntil(() => gotTaken || gotEmpty);
            bool won = gotTaken;
            wonRace = won;
            Signal("pickup", won ? "Taken" : "Empty");
            yield return new WaitUntil(() => Exists("A.pickup") && Exists("B.pickup"));
            if (Marker("A.pickup") == Marker("B.pickup"))
            {
                Fail("Both players received the same race outcome");
                yield break;
            }
            Phase("return item and transfer ownership");
            if (won)
            {
                yield return new WaitUntil(() => bag.Any(item => item.Id == contested));
                Quick(true, bag.Single(i => i.Id == contested));
                yield return new WaitUntil(() => bag.Length == 0 && crate.Any(item => item.Id == contested));
                Signal("returned");
                yield return new WaitUntil(() => Exists("A.claimed") || Exists("B.claimed"));
                foreach (var other in completeCrate.Skip(1).Take(2))
                {
                    Quick(false, crate.Single(i => i.Id == other.Id));
                    yield return new WaitUntil(() => bag.Any(item => item.Id == other.Id));
                }
            }
            else
            {
                yield return new WaitUntil(() => Exists("A.returned") || Exists("B.returned"));
                yield return new WaitUntil(() => crate.Any(item => item.Id == contested));
                var target = crate.Single(i => i.Id == contested);
                Client.ProbeHover(false, target.X + .5f, target.Y + .5f);
                Client.ProbeSelectOtherCrateItem(contested);
                uint selected = Client.ProbeSelectedItem;
                if (selected == 0 || selected == contested) { Fail("Could not establish a distinct keyboard selection"); yield break; }
                Client.ProbeTransferKey(true);
                Client.ProbeTransferKey(false);
                yield return new WaitUntil(() => bag.Any(item => item.Id == contested));
                Signal("pointer-tap", selected + "," + contested);
                Signal("claimed");
            }
            string expected = Contents(won ? completeCrate.Skip(1).Take(2).ToArray() : completeCrate.Take(1).ToArray());
            if (Contents(bag) != expected)
            {
                Fail("Final bag did not match item redistribution");
                yield break;
            }
            Signal("inventory", expected);
            yield return new WaitUntil(() => Exists("A.inventory") && Exists("B.inventory"));
            yield return new WaitUntil(() => crate.Length == completeCrate.Length - 3);
            yield return new WaitForSecondsRealtime(0.20f);
            Capture("inventory");
            yield return new WaitForSecondsRealtime(0.25f);
            int previous = closes;
            link.Close();
            yield return new WaitUntil(() => closes > previous);
            yield return TestPersonalGrid();
        }

        private IEnumerator Run()
        {
            Phase("first authoritative pose");
            yield return new WaitUntil(() => havePose);
            Signal("connected", network.LocalClientId.ToString(CultureInfo.InvariantCulture));
            Phase("both connected");
            yield return new WaitUntil(() => Exists("A.connected") && Exists("B.connected"));
            bool chartHouse = Array.Exists(Environment.GetCommandLineArgs(), arg => arg == "-probeChartHouse");
            bool warehouse = Array.Exists(Environment.GetCommandLineArgs(), arg => arg == "-probeWarehouse");
            if (warehouse || chartHouse || Array.Exists(Environment.GetCommandLineArgs(), arg => arg == "-probeSurfaces"))
            {
                if (warehouse) yield return CheckWarehouse();
                else yield return chartHouse ? CheckChartHouse() : CheckFreightSurfaces();
                finished = true;
                Signal("done");
                Application.Quit(0);
                yield break;
            }
            Capture("bay");

            if (role == "A")
            {
                if (Vector2.Distance(position, RaidMap.Spawn(0)) > 0.2f || RaidMap.CanOpen(position))
                {
                    Fail("Unexpected blocked-side spawn " + position);
                    yield break;
                }
                // Stand within reach on the hidden side before testing the partition.
                yield return Walk(new Vector2(0f, -1f), 0f);
                Phase("wall denial");
                link.Open();
                yield return new WaitUntil(() => blocked);
                Signal("wall-denied");
                yield return new WaitUntil(() => Exists("server-before-search.checked"));
                yield return CheckVisibility();
                if (captureFrames)
                {
                    yield return Walk(new Vector2(-3.9f, 3.8f));
                    yield return CapturePerspectives();
#if !UNITY_SERVER
                    yield return CaptureLocomotion();
#endif
                }
                yield return Walk(new Vector2(1.15f, 1.7f));
            }
            else
            {
                if (Vector2.Distance(position, RaidMap.Spawn(1)) > 0.2f)
                {
                    Fail("Unexpected nearby-side spawn " + position);
                    yield break;
                }
                Phase("wait next to A without opening");
                // A fixed nonzero yaw exposes an erroneous identity rotation on reappearance.
                Client.SetProbeMotion(Vector2.zero, 126f, 3f);
                yield return new WaitUntil(() => Exists("A.moving-peek"));
                // Sample the interval between a visible shoulder and the centre
                // clearing cover over several server ticks on this outward pass.
                yield return Walk(new Vector2(2.70f, 1.25f), 126f, 2.625f, .10f);
                yield return Walk(RaidMap.Spawn(1), 126f, 2.75f);
                Signal("peek-returned");
                Phase("wait for crate access");
                yield return new WaitUntil(() => Exists("A.opened"));
                yield return new WaitForSecondsRealtime(0.65f);
            }

            if (crateFrames != 0 || !RaidMap.CanOpen(position))
            {
                Fail("Unexpected crate frame count or position before opening: " + crateFrames + " at " + position);
                yield break;
            }
            yield return Search();
            yield return Trade();
            if (role == "A") yield return TestPauseMenu();
            else
            {
                Phase("finish current raid after A returns to base");
                yield return new WaitUntil(() => Exists("A.waiting-next"));
                if (Client.ProbeRound != 1 || Contents(bag) != Marker("B.inventory"))
                { Fail("A changed B's active raid or bag"); yield break; }
            }
            // Give each camera its own position inside the same extraction pad.
            float side = role == "A" ? -0.51f : 0.51f;
            yield return Walk(new Vector2(RaidMap.Exit.x + side, 5f), sprint: true);
            yield return Walk(RaidMap.Exit + new Vector2(side, 0f), sprint: true);
            Phase("three-second evacuation");
            yield return new WaitUntil(() => result != null);
            if (Contents(result) != Marker(role + ".inventory"))
            {
                Fail("Private evacuation result does not match pickup");
                yield break;
            }
            Signal("exit-ms", (observedHold * 1000).ToString("F0", CultureInfo.InvariantCulture));
            Signal("result", Contents(result));
            Capture("result");
            yield return Replay();
            yield return CheckLocomotion();
            finished = true;
            Signal("done");
            Application.Quit(0);
        }
    }
}

#endif
