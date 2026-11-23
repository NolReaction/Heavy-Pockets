using UnityEngine;

namespace HeavyPockets
{
    internal enum RaidScreen { Play, Menu, Wardrobe, Receipt, Base }
    internal enum RaidMenuAction { Resume, Bag, Wardrobe, Base, Join, CancelQueue, Quit, Pouch1, Pouch2, Pouch3, Pouch4 }

    public sealed partial class RaidClient
    {
        private byte basePouchLevel = 1;
        private bool InPauseMenu => screen == RaidScreen.Menu || screen == RaidScreen.Wardrobe;

        private void LeaveWardrobe(bool apply)
        {
            if (screen != RaidScreen.Wardrobe) return;
            if (apply) SaveStyle(hud.WardrobeStyle);
            hud.CloseWardrobe();
            screen = RaidScreen.Menu;
            hud.ResetMenuInput();
        }

        internal void EscapePressed()
        {
            if (disconnected || !HasOwnPose) return;
            if (screen == RaidScreen.Wardrobe) { LeaveWardrobe(false); return; }
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
            if (action >= RaidMenuAction.Pouch1 && action <= RaidMenuAction.Pouch4)
            {
                if (screen == RaidScreen.Base && extracted && !disconnected && !queued && !changingQueue)
                    Link.SetPouchLevel((byte)(1 + (int)action - (int)RaidMenuAction.Pouch1));
                return;
            }
            switch (action)
            {
                case RaidMenuAction.Wardrobe when screen == RaidScreen.Menu:
                    hud.OpenWardrobe(SelectedStyle, view != null, LeaveWardrobe);
                    screen = RaidScreen.Wardrobe;
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
        internal int ProbeWardrobeStyle => hud.WardrobeStyle;
        internal void ProbeWardrobeKey(int rowStep, int change, bool enter = false)
        {
            if (ScriptedInput && screen == RaidScreen.Wardrobe) hud.WardrobeKey(rowStep, change, enter);
        }
#endif
    }
}
