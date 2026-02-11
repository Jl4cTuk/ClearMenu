using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;
using MonoMod.Cil;
using MonoMod.Utils;

namespace Celeste.Mod.ClearMenu;

public class ClearMenuModule : EverestModule {
    public static ClearMenuModule Instance { get; private set; }
    public override Type SettingsType => typeof(ClearMenuModuleSettings);
    public static ClearMenuModuleSettings Settings => (ClearMenuModuleSettings) Instance._Settings;

    private const string ModOptionsButtonTypeName = "Celeste.Mod.UI.MainMenuModOptionsButton";
    private const string ClimbButtonTypeName = "Celeste.MainMenuClimb";

    private static readonly string[] TargetLabelKeys = {
        "MENU_CREDITS",
        "MENU_PICO8",
        "MENU_OPTIONS",
        "MENU_EXIT"
    };

    private static readonly string[] TargetOnConfirmMethods = {
        "OnCredits",
        "OnPico8",
        "OnOptions",
        "OnExit"
    };

    private static bool loggedMenuButtons;
    private static Action onOptionsAction;
    private static Action onModOptionsAction;
    private static bool forceSelectFirstButton;

    public ClearMenuModule() {
        Instance = this;
#if DEBUG
        Logger.SetLogLevel(nameof(ClearMenuModule), LogLevel.Verbose);
#else
        Logger.SetLogLevel(nameof(ClearMenuModule), LogLevel.Info);
#endif
    }

    public override void Load() {
        Everest.Events.MainMenu.OnCreateButtons += OnMainMenuCreateButtons;
        IL.Celeste.Overworld.InputEntity.Render += OnOverworldInputEntityRenderIL;
        On.Celeste.OuiMainMenu.Update += OnOuiMainMenuUpdate;
    }

    public override void Unload() {
        Everest.Events.MainMenu.OnCreateButtons -= OnMainMenuCreateButtons;
        IL.Celeste.Overworld.InputEntity.Render -= OnOverworldInputEntityRenderIL;
        On.Celeste.OuiMainMenu.Update -= OnOuiMainMenuUpdate;
    }

    private static void OnOverworldInputEntityRenderIL(ILContext il) {
        MethodInfo target = typeof(ButtonUI).GetMethod(
            "Render",
            BindingFlags.Static | BindingFlags.Public,
            null,
            new[] { typeof(Vector2), typeof(string), typeof(VirtualButton), typeof(float), typeof(float), typeof(float), typeof(float) },
            null
        );
        MethodInfo replacement = typeof(ClearMenuModule).GetMethod(
            nameof(RenderButtonPromptFiltered),
            BindingFlags.Static | BindingFlags.NonPublic
        );
        if (target == null || replacement == null) {
            return;
        }

        ILCursor cursor = new ILCursor(il);
        while (cursor.TryGotoNext(MoveType.After, instr => instr.MatchCall(target))) {
            cursor.Prev.Operand = replacement;
        }
    }

    private static void RenderButtonPromptFiltered(
        Vector2 position,
        string text,
        VirtualButton button,
        float scale,
        float alpha,
        float textScale,
        float wiggle
    ) {
        if (Settings.Enabled && (button == Input.MenuCancel || button == Input.MenuConfirm)) {
            return;
        }
        ButtonUI.Render(position, text, button, scale, alpha, textScale, wiggle);
    }

    private static void OnMainMenuCreateButtons(OuiMainMenu menu, List<MenuButton> buttons) {
        if (buttons == null || buttons.Count == 0) {
            return;
        }
        LogMenuButtonsOnce(buttons);
        if (!Settings.Enabled) {
            return;
        }
        CaptureMenuActions(menu, buttons);
        RemoveTargetButtons(menu, buttons);
        if (forceSelectFirstButton) {
            if (TrySelectFirstButton(menu, buttons)) {
                forceSelectFirstButton = false;
            }
        }
    }

    private static void OnOuiMainMenuUpdate(On.Celeste.OuiMainMenu.orig_Update orig, OuiMainMenu self) {
        orig(self);

        if (!self.Focused) {
            return;
        }
        if (!Settings.Enabled) {
            return;
        }

        if (forceSelectFirstButton) {
            List<MenuButton> buttons = GetButtons(self);
            if (TrySelectFirstButton(self, buttons)) {
                forceSelectFirstButton = false;
            }
        }

        if (MInput.Keyboard.Pressed(Keys.D1)) {
            onOptionsAction?.Invoke();
        }
        if (MInput.Keyboard.Pressed(Keys.D2)) {
            onModOptionsAction?.Invoke();
        }
    }

    private static void CaptureMenuActions(OuiMainMenu menu, List<MenuButton> buttons) {
        onOptionsAction = null;
        onModOptionsAction = null;

        foreach (MenuButton button in buttons) {
            if (button == null || button.OnConfirm == null) {
                continue;
            }

            string typeName = button.GetType().FullName ?? "";
            if (onModOptionsAction == null &&
                string.Equals(typeName, ModOptionsButtonTypeName, StringComparison.Ordinal)) {
                onModOptionsAction = button.OnConfirm;
            }

            if (onOptionsAction == null &&
                button.OnConfirm.Target == menu &&
                string.Equals(button.OnConfirm.Method.Name, "OnOptions", StringComparison.Ordinal)) {
                onOptionsAction = button.OnConfirm;
            }

            if (onOptionsAction != null && onModOptionsAction != null) {
                break;
            }
        }
    }

    private static void RemoveTargetButtons(OuiMainMenu menu, List<MenuButton> buttons) {
        if (buttons == null || buttons.Count == 0) {
            return;
        }

        int selectedIndex = GetSelectedIndex(menu);
        bool selectionRemoved = false;
        bool removed = false;
        List<MenuButton> removedButtons = null;
        for (int i = buttons.Count - 1; i >= 0; i--) {
            MenuButton button = buttons[i];
            if (IsTargetButton(menu, button)) {
                if (i == selectedIndex) {
                    selectionRemoved = true;
                }
                if (i < selectedIndex) {
                    selectedIndex--;
                }
                removedButtons ??= new List<MenuButton>();
                removedButtons.Add(button);
                buttons.RemoveAt(i);
                removed = true;
            }
        }

        if (removed && removedButtons != null) {
            foreach (MenuButton removedButton in removedButtons) {
                RelinkNeighbors(buttons, removedButton);
                removedButton.RemoveSelf();
            }
        }

        if (removed && (selectionRemoved || selectedIndex < 0 || selectedIndex >= buttons.Count)) {
            EnsureSelection(menu, buttons);
        }
    }

    private static int GetSelectedIndex(OuiMainMenu menu) {
        try {
            return new DynamicData(menu).Get<int>("selectedIndex");
        }
        catch {
            return -1;
        }
    }

    private static List<MenuButton> GetButtons(OuiMainMenu menu) {
        try {
            return new DynamicData(menu).Get<List<MenuButton>>("buttons");
        }
        catch {
            return null;
        }
    }

    private static void LogMenuButtonsOnce(List<MenuButton> buttons) {
        if (loggedMenuButtons) {
            return;
        }
        if (buttons == null || buttons.Count == 0) {
            return;
        }

        loggedMenuButtons = true;

        Logger.Info(nameof(ClearMenuModule), "Main menu buttons:");
        for (int i = 0; i < buttons.Count; i++) {
            MenuButton button = buttons[i];
            if (button == null) {
                Logger.Info(nameof(ClearMenuModule), $"  [{i}] <null>");
                continue;
            }

            string typeName = button.GetType().FullName ?? "<unknown>";
            string labelName = GetStringProperty(button, "LabelName") ?? "<none>";
            string label = GetStringField(button, "label") ?? "<none>";
            string onConfirm = button.OnConfirm?.Method?.Name ?? "<none>";

            Logger.Info(nameof(ClearMenuModule),
                $"  [{i}] type={typeName} labelName={labelName} label={label} onConfirm={onConfirm}");
        }
    }

    private static bool IsTargetButton(OuiMainMenu menu, MenuButton button) {
        if (button == null) {
            return false;
        }

        string typeName = button.GetType().FullName ?? "";
        if (string.Equals(typeName, ModOptionsButtonTypeName, StringComparison.Ordinal)) {
            return true;
        }

        if (button.OnConfirm != null && button.OnConfirm.Target == menu) {
            string methodName = button.OnConfirm.Method.Name;
            for (int i = 0; i < TargetOnConfirmMethods.Length; i++) {
                if (string.Equals(methodName, TargetOnConfirmMethods[i], StringComparison.Ordinal)) {
                    return true;
                }
            }
        }

        string labelName = GetStringProperty(button, "LabelName");
        if (!string.IsNullOrEmpty(labelName)) {
            for (int i = 0; i < TargetLabelKeys.Length; i++) {
                if (labelName.Equals(TargetLabelKeys[i], StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }
        }

        string label = GetStringField(button, "label");
        if (!string.IsNullOrEmpty(label)) {
            for (int i = 0; i < TargetLabelKeys.Length; i++) {
                string targetText = Dialog.Clean(TargetLabelKeys[i]);
                if (string.Equals(label, targetText, StringComparison.Ordinal)) {
                    return true;
                }
            }
        }

        return false;
    }

    private static void RelinkNeighbors(List<MenuButton> buttons, MenuButton removed) {
        if (removed == null || buttons == null) {
            return;
        }

        MenuButton up = removed.UpButton;
        MenuButton down = removed.DownButton;
        MenuButton left = removed.LeftButton;
        MenuButton right = removed.RightButton;

        foreach (MenuButton b in buttons) {
            if (b.UpButton == removed) b.UpButton = up;
            if (b.DownButton == removed) b.DownButton = down;
            if (b.LeftButton == removed) b.LeftButton = left;
            if (b.RightButton == removed) b.RightButton = right;
        }
    }

    private static void EnsureSelection(OuiMainMenu menu, List<MenuButton> buttons) {
        if (buttons == null || buttons.Count == 0) {
            return;
        }

        bool anySelected = false;
        foreach (MenuButton b in buttons) {
            if (b != null && b.Selected) {
                anySelected = true;
                break;
            }
        }
        if (anySelected) {
            return;
        }

        int targetIndex = -1;
        for (int i = 0; i < buttons.Count; i++) {
            MenuButton b = buttons[i];
            if (b != null && string.Equals(b.GetType().FullName, ClimbButtonTypeName, StringComparison.Ordinal)) {
                targetIndex = i;
                break;
            }
        }
        if (targetIndex == -1) {
            for (int i = 0; i < buttons.Count; i++) {
                if (buttons[i] != null) {
                    targetIndex = i;
                    break;
                }
            }
        }
        if (targetIndex != -1) {
            MenuButton target = buttons[targetIndex];
            MenuButton.ClearSelection(menu.Scene);
            target.Selected = true;
            SetSelectedIndex(menu, targetIndex);
        }
    }

    private static bool TrySelectFirstButton(OuiMainMenu menu, List<MenuButton> buttons) {
        if (buttons == null || buttons.Count == 0) {
            return false;
        }

        int targetIndex = -1;
        for (int i = 0; i < buttons.Count; i++) {
            if (buttons[i] != null) {
                targetIndex = i;
                break;
            }
        }
        if (targetIndex == -1) {
            return false;
        }

        MenuButton target = buttons[targetIndex];
        if (target.Scene == null) {
            return false;
        }
        MenuButton.ClearSelection(menu.Scene);
        target.Selected = true;
        SetSelectedIndex(menu, targetIndex);
        return true;
    }

    private static void SetSelectedIndex(OuiMainMenu menu, int index) {
        try {
            new DynamicData(menu).Set("selectedIndex", index);
        }
        catch {
            // ignore reflection failures
        }
    }

    private static string GetStringProperty(object obj, string name) {
        try {
            PropertyInfo prop = obj.GetType().GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
            if (prop != null && prop.PropertyType == typeof(string)) {
                return (string)prop.GetValue(obj);
            }
        }
        catch {
            // ignore reflection failures
        }
        return null;
    }

    private static string GetStringField(object obj, string name) {
        try {
            FieldInfo field = obj.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
            if (field != null && field.FieldType == typeof(string)) {
                return (string)field.GetValue(obj);
            }
        }
        catch {
            // ignore reflection failures
        }
        return null;
    }

    internal static void RequestMainMenuRebuild() {
        if (Engine.Scene is not Overworld overworld) {
            return;
        }

        OuiMainMenu menu = overworld.GetUI<OuiMainMenu>();
        menu?.NeedsRebuild();
    }

    internal static void RequestSelectFirstButton() {
        forceSelectFirstButton = true;
    }

}
