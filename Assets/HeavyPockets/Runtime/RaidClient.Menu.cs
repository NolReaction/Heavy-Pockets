using UnityEngine;

namespace HeavyPockets
{
    internal enum RaidScreen { Play, Menu, Animations, Receipt, Base, Lighting }
    internal enum RaidMenuAction { Resume, Bag, Animations, Base, Join, CancelQueue, Quit, Pouch1, Pouch2, Pouch3, Pouch4,
        HarborDaylight, HarborPoster, HarborDusk, Lighting, BackToMenu }

    public sealed partial class RaidClient
    {
        private byte basePouchLevel = 1;
        private bool lightingLook;
        private bool InPauseMenu => screen == RaidScreen.Menu || screen == RaidScreen.Animations || screen == RaidScreen.Lighting;

        private void CloseMenuViews()
        {
            hud?.CloseAnimationPreview();
            hud?.CloseLightingControls();
            lightingLook = false;
        }

        private void UpdateLightingLook()
        {
            if (Input.GetMouseButtonDown(1)) lightingLook = !RaidHud.PointerOverLightingControls();
            if (!lightingLook) return;
            if (Input.GetMouseButton(1))
            {
                RunnerInput.Look(ref yaw, ref pitch);
                if (Time.unscaledTime < nextSend) return;
            }
            else lightingLook = false;
            SendMovement(Vector2.zero);
        }

        private void LeaveAnimationPreview()
        {
            if (screen != RaidScreen.Animations) return;
            hud.CloseAnimationPreview();
            screen = RaidScreen.Menu;
            hud.ResetMenuInput();
        }

        internal void EscapePressed()
        {
            if (disconnected || !HasOwnPose) return;
            if (screen == RaidScreen.Lighting) { UseMenu(RaidMenuAction.BackToMenu); return; }
            if (screen == RaidScreen.Animations) { LeaveAnimationPreview(); return; }
            if (screen == RaidScreen.Receipt) { UseMenu(RaidMenuAction.Base); return; }
            if (screen == RaidScreen.Menu) { UseMenu(RaidMenuAction.Resume); return; }
            if (screen != RaidScreen.Play || extracted) return;
            if (inventoryOpen)
            {
                if (!hud.CancelDrag()) CloseInventory(true);
                return;
            }
            screen = RaidScreen.Menu;
            hud.ResetSelection();
            hud.ResetMenuInput();
            SendMovement(Vector2.zero);
            ReleaseCursor();
        }

        internal void UseMenu(RaidMenuAction action)
        {
            if (action >= RaidMenuAction.HarborDaylight && action <= RaidMenuAction.HarborDusk)
            {
                if (screen == RaidScreen.Lighting)
                    RaidScenery.SetEnvironmentStyle((int)action - (int)RaidMenuAction.HarborDaylight);
                return;
            }
            if (action >= RaidMenuAction.Pouch1 && action <= RaidMenuAction.Pouch4)
            {
                if (screen == RaidScreen.Base && extracted && !disconnected && !queued && !changingQueue)
                    Link.SetPouchLevel((byte)(1 + (int)action - (int)RaidMenuAction.Pouch1));
                return;
            }
            switch (action)
            {
                case RaidMenuAction.Lighting when screen == RaidScreen.Menu:
                    lightingLook = false;
                    hud.OpenLightingControls();
                    screen = RaidScreen.Lighting;
                    break;
                case RaidMenuAction.BackToMenu when screen == RaidScreen.Lighting:
                    hud.CloseLightingControls();
                    if (lightingLook) SendMovement(Vector2.zero);
                    lightingLook = false;
                    screen = RaidScreen.Menu;
                    hud.ResetMenuInput();
                    break;
                case RaidMenuAction.Animations when screen == RaidScreen.Menu:
                    hud.OpenAnimationPreview(view != null, LeaveAnimationPreview);
                    screen = RaidScreen.Animations;
                    break;
                case RaidMenuAction.Resume when screen == RaidScreen.Menu:
                    screen = RaidScreen.Play;
                    if (!ScriptedInput && Application.isFocused && view != null) CaptureCursor();
                    break;
                case RaidMenuAction.Bag when screen == RaidScreen.Menu:
                    screen = RaidScreen.Play;
                    bagFromMenu = true;
                    ToggleInventory();
                    break;
                case RaidMenuAction.Base when screen == RaidScreen.Receipt:
                    Link.EnterBase();
                    screen = RaidScreen.Base;
                    break;
                case RaidMenuAction.Join when screen == RaidScreen.Base && extracted && !queued && !changingQueue:
                case RaidMenuAction.CancelQueue when screen == RaidScreen.Base && extracted && queued && !changingQueue:
                    if (disconnected) break;
                    changingQueue = true;
                    Link.SetNextRaidReady(round, action == RaidMenuAction.Join);
                    break;
                case RaidMenuAction.Quit when disconnected || screen == RaidScreen.Menu || screen == RaidScreen.Base:
                    Application.Quit();
                    break;
            }
        }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        internal RaidScreen ProbeScreen => screen;
        internal bool ProbeInventoryOpen => inventoryOpen;
        internal bool ProbeQueued => queued && !changingQueue;
        internal bool ProbeCanJoin => screen == RaidScreen.Base && !queued && !changingQueue;
        internal uint ProbeRound => round;
        internal bool ProbeMenuClick(RaidMenuAction action) => ScriptedInput && hud.ProbeMenuClick(InventoryState(), action);
        internal StarterMotion ProbeAnimation => hud.PreviewMotion;
        internal void ProbeAnimationKey(int switchClip)
        {
            if (ScriptedInput && screen == RaidScreen.Animations) hud.AnimationPreviewKey(switchClip, 0, false);
        }
#endif
    }
}
