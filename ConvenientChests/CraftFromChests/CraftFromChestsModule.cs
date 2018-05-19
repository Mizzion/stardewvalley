﻿using System;
using System.Collections.Generic;
using System.Linq;
using ConvenientChests.CategorizeChests;
using ConvenientChests.StackToNearbyChests;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;
using Utility = StardewValley.Utility;

namespace ConvenientChests.CraftFromChests {
    public class CraftFromChestsModule : Module {
        private GameMenu           GameMenu         { get; set; }
        private int                UserSlots        { get; set; }
        private int                UserSlotsEmpty   { get; set; }
        private IList<Item>        UserInventory    { get; set; }
        private IList<IList<Item>> CraftInventories { get; set; }
        private List<List<Item>> CraftInventoriesInitial { get; set; }

        private        bool IsCraftingScreen => GameMenu?.currentTab == GameMenu.craftingTab;
        private        bool IsCookingScreen  => IsCraftingScreen && GameMenu.GetType() == Type.GetType("CookingSkill.NewCraftingPage, CookingSkill");
        private static bool HasFridge        => Utility.getHomeOfFarmer(Game1.player).upgradeLevel > 0;

        public CraftFromChestsModule(ModEntry modEntry) : base(modEntry) { }

        public override void activate() {
            GameMenuExtension.Shown      += OnGameMenuShown;
            MenuEvents.MenuClosed        += OnMenuClosed;
            GameMenuExtension.TabChanged += OnGameMenuTabChanged;
        }

        private void Hijack() {
            var nearbyChests = Game1.player.GetNearbyChests(Config.CraftRadius).Where(c => c.items.Any(i => i != null)).ToList();
            if (!nearbyChests.Any())
                return;

            try {
                CombineInventories(nearbyChests);

                var tabs = GameMenu.getTabs();
                var page = (StardewValley.Menus.CraftingPage) tabs[GameMenu.craftingTab];
                page.inventory.actualInventory = UserInventory;

                GameEvents.HalfSecondTick += UpdateEvent;
            }
            catch (Exception e) {
                Monitor.Log("Something went wrong! Consider disabling CraftFromChests:", LogLevel.Alert);
                Monitor.Log(e.ToString(), LogLevel.Error);
                Restore();
            }
        }

        private void Restore() {
            if (UserInventory == null)
                return;

            // remove cleanup handlers
            GameEvents.HalfSecondTick -= UpdateEvent;

            // clean one final time
            CleanupInventories();

            // restore inventory
            Game1.player.Items    = UserInventory;
            Game1.player.MaxItems = UserSlots;
            UserSlots             = 0;
            UserSlotsEmpty        = 0;
            UserInventory         = null;
            CraftInventories      = null;
        }

        private void CleanupInventories() {
            if (UserInventory == null || CraftInventories == null)
                return;

            // check for used up items
            foreach (var c in CraftInventories)
                for (var i = 0; i < c.Count; i++)
                    if (c[i]?.Stack < 1)
                        c[i] = null;

            // check for new items
            var emptySlots = UserSlotsEmpty;
            for (var i = 1; i <= emptySlots; i++)
            {
                var item = Game1.player.Items[Game1.player.MaxItems - i];
                if (item == null) continue;

                var index = UserInventory.IndexOf(null);
                if (index == -1)
                    // No more empty slots
                    // -> add to hand
                    return;

                // add a copy to user inventory
                UserInventory[index] = item;
                // and reduce empty slots for next check
                UserSlotsEmpty--;
            }
            
            // check for changed items
            if (!Config.CraftToChests)
                for (int i = 1; i < CraftInventories.Count; i++)
                {
                    var invNew = CraftInventories[i];
                    var invOld = CraftInventoriesInitial[i];
    
                    for (int j = 0; j < invNew.Count; j++)
                    {
                        var iOld = invOld[j];
                        var iNew = invNew[j];
                        
                        if (iNew == null)
                            continue;
    
                        if (iOld == null) {
                            invOld[j] = iNew.GetCopy();
                            continue;
                        }
    
                        if (iNew.Stack == iOld.Stack) 
                            continue;
    
                        if (iNew.Stack < iOld.Stack) {
                            // Crafted from stack
                            modEntry.Monitor.Log($"Stack decreased: {iOld.Name} {iOld.Stack} -> {iNew.Stack}");
                            iOld.Stack = iNew.Stack;
                        }
                        else {
                            // Crafted TO stack
                            modEntry.Monitor.Log($"Stack increased: {iOld.Name} {iOld.Stack} -> {iNew.Stack}");
                            
                            var o   = iNew.GetCopy();
                            o.Stack -= iOld.Stack;
    
                            var item  = UserInventory.FirstOrDefault(x => x?.canStackWith(iNew) == true && (x.Stack + o.Stack) <= o.maximumStackSize());
                            var index = UserInventory.IndexOf(item);
                            if (index == -1) {
                                // Can not move stack
                                // -> leave it in chest
                                modEntry.Monitor.Log($"Inv full!");
                                iOld.Stack = iNew.Stack;
                                continue;
                            }
                            
                            modEntry.Monitor.Log($"Moved stack: {o.Name} {o.Stack}/{iNew.Stack} to inventory ({index}: {UserInventory[index]?.Name ?? "null"})!");
    
                            if (UserInventory[index] == null) {
                                modEntry.Monitor.Log($"Moved to new stack");
    
                                UserInventory[index] = o;
                                UserSlotsEmpty--;
                            } 
                            else {
                                modEntry.Monitor.Log($"Moved to old stack: {UserInventory[index].Stack} -> {UserInventory[index].Stack + o.Stack}");
                                UserInventory[index].Stack += o.Stack;
                            }
    
                            // update items
                            iNew.Stack = iOld.Stack;
                            modEntry.Monitor.Log($"Set chest stack to {iNew.Stack}");
                        }
                    }
            }
        }

        private void CombineInventories(IEnumerable<Chest> chests) {
            if (UserInventory != null)
                return;

            // store original inventory...
            UserSlots               = Game1.player.MaxItems;
            UserSlotsEmpty          = Game1.player.freeSpotsInInventory();
            UserInventory           = Game1.player.Items.ToList();
            CraftInventories        = GetInventories(chests).ToList();
                CraftInventoriesInitial = CraftInventories.Select(l => l.Select(ItemHelper.GetCopy).ToList()).ToList();

            // ... and replace with combined inventory
            Game1.player.Items    = new List<Item>(CraftInventories.SelectMany(l => l).Where(i => i != null));
            Game1.player.MaxItems = Game1.player.Items.Count + UserSlotsEmpty;

            // ... add empty slots
            for (int i=0; i < UserSlotsEmpty; i++)
                Game1.player.Items.Add(null);
        }

        private IEnumerable<IList<Item>> GetInventories(IEnumerable<Chest> chests) {
            yield return UserInventory;

            foreach (var c in chests)
                yield return c.items;

            // always add fridge when on cooking screen
            if (IsCookingScreen && HasFridge)
                yield return Utility.getHomeOfFarmer(Game1.player).fridge.Value.items;
        }

        private void OnGameMenuShown(object sender, EventArgs e) {
            GameMenu = Game1.activeClickableMenu as GameMenu;

            if (IsCraftingScreen)
                Hijack();
        }

        private void OnGameMenuTabChanged(object sender, EventArgs e) {
            if (IsCraftingScreen)
                Hijack();

            else
                Restore();
        }

        private void OnMenuClosed(object sender, EventArgsClickableMenuClosed e) {
            if (GameMenu == null)
                return;

            Restore();
        }

        private void UpdateEvent(object sender, EventArgs e) => CleanupInventories();
    }
}