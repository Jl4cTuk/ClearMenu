using System;
using System.Collections.Generic;
using System.Reflection;
using FMOD.Studio;
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

    public override void CreateModMenuSection(TextMenu menu, bool inGame, EventInstance snapshot) {
        menu.Add(new TextMenu.SubHeader(Dialog.Clean("ClearMenu") + " | v." + Instance.Metadata.VersionString));

        EaseInSubMenu menuItemsSubMenu = new EaseInSubMenu(Dialog.Clean("ClearMenu_Setting_MenuItems"), false);
        menuItemsSubMenu.Add(new TextMenu.OnOff(Dialog.Clean("ClearMenu_Setting_HidePico8"), Settings.HidePico8).Change(value => {
                Settings.HidePico8 = value;
                RequestMainMenuRebuild();
            }));
        menuItemsSubMenu.Add(new TextMenu.OnOff(Dialog.Clean("ClearMenu_Setting_HideOptions"), Settings.HideOptions).Change(value => {
                Settings.HideOptions = value;
                RequestMainMenuRebuild();
            }));
        menuItemsSubMenu.Add(new TextMenu.OnOff(Dialog.Clean("ClearMenu_Setting_HideModOptions"), Settings.HideModOptions).Change(value => {
                Settings.HideModOptions = value;
                RequestMainMenuRebuild();
            }));
        menuItemsSubMenu.Add(new TextMenu.OnOff(Dialog.Clean("ClearMenu_Setting_HideCredits"), Settings.HideCredits).Change(value => {
                Settings.HideCredits = value;
                RequestMainMenuRebuild();
            }));
        menuItemsSubMenu.Add(new TextMenu.OnOff(Dialog.Clean("ClearMenu_Setting_HideExit"), Settings.HideExit).Change(value => {
                Settings.HideExit = value;
                RequestMainMenuRebuild();
            }));
        menuItemsSubMenu.Add(new TextMenu.OnOff(Dialog.Clean("ClearMenu_Setting_HideButtonTips"), Settings.HideButtonTips).Change(value => {
                Settings.HideButtonTips = value;
            }));

        TextMenu.Item enabledToggle = new TextMenu.OnOff(Dialog.Clean("ClearMenu_Setting_Enabled"), Settings.Enabled).Change(value => {
            Settings.Enabled = value;
            menuItemsSubMenu.FadeVisible = value;
            if (!value) {
                menuItemsSubMenu.Focused = false;
            }
        });
        menu.Add(enabledToggle);
        menuItemsSubMenu.FadeVisible = Settings.Enabled;
        menu.Add(menuItemsSubMenu);
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
        if (Settings.Enabled && Settings.HideButtonTips && (button == Input.MenuCancel || button == Input.MenuConfirm)) {
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

        if (Settings.HideModOptions && MatchesButtonType(button, ModOptionsButtonTypeName)) {
            return true;
        }
        if (Settings.HideCredits && MatchesMenuButton(menu, button, "OnCredits", "MENU_CREDITS")) {
            return true;
        }
        if (Settings.HidePico8 && MatchesMenuButton(menu, button, "OnPico8", "MENU_PICO8")) {
            return true;
        }
        if (Settings.HideOptions && MatchesMenuButton(menu, button, "OnOptions", "MENU_OPTIONS")) {
            return true;
        }
        if (Settings.HideExit && MatchesMenuButton(menu, button, "OnExit", "MENU_EXIT")) {
            return true;
        }

        return false;
    }

    private static bool MatchesButtonType(MenuButton button, string targetTypeName) {
        string typeName = button.GetType().FullName ?? "";
        return string.Equals(typeName, targetTypeName, StringComparison.Ordinal);
    }

    private static bool MatchesMenuButton(OuiMainMenu menu, MenuButton button, string methodName, string labelKey) {
        if (button.OnConfirm != null &&
            button.OnConfirm.Target == menu &&
            string.Equals(button.OnConfirm.Method.Name, methodName, StringComparison.Ordinal)) {
            return true;
        }

        string labelName = GetStringProperty(button, "LabelName");
        if (!string.IsNullOrEmpty(labelName) &&
            labelName.Equals(labelKey, StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        string label = GetStringField(button, "label");
        if (!string.IsNullOrEmpty(label) &&
            string.Equals(label, Dialog.Clean(labelKey), StringComparison.Ordinal)) {
            return true;
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

internal class EaseInSubMenu : TextMenuExt.SubMenu {
    public bool FadeVisible { get; set; }
    private float alpha;
    private float unEasedAlpha;
    private readonly MTexture icon;

    public EaseInSubMenu(string label, bool enterOnSelect) : base(label, enterOnSelect) {
        alpha = unEasedAlpha = ClearMenuModule.Settings.Enabled ? 1f : 0f;
        FadeVisible = Visible = ClearMenuModule.Settings.Enabled;
        icon = GFX.Gui["downarrow"];
    }

    public override float Height() => MathHelper.Lerp(-Container.ItemSpacing, base.Height(), alpha);

    public override void Update() {
        base.Update();

        float targetAlpha = FadeVisible ? 1f : 0f;
        if (Math.Abs(unEasedAlpha - targetAlpha) > 0.001f) {
            unEasedAlpha = Calc.Approach(unEasedAlpha, targetAlpha, Engine.RawDeltaTime * 3f);
            alpha = FadeVisible ? Ease.SineOut(unEasedAlpha) : Ease.SineIn(unEasedAlpha);
        }

        Visible = alpha != 0f;
    }

    public override void Render(Vector2 position, bool highlighted) {
        Vector2 top = new(position.X, position.Y - (Height() / 2f));

        float currentAlpha = Container.Alpha * alpha;
        Color color = Disabled ? Color.DarkSlateGray : ((highlighted ? Container.HighlightColor : Color.White) * currentAlpha);
        Color strokeColor = Color.Black * (currentAlpha * currentAlpha * currentAlpha);

        bool unCentered = Container.InnerContent == TextMenu.InnerContentMode.TwoColumn && !AlwaysCenter;

        Vector2 titlePosition = top + (Vector2.UnitY * TitleHeight / 2f) + (unCentered ? Vector2.Zero : new Vector2(Container.Width * 0.5f, 0f));
        Vector2 justify = unCentered ? new Vector2(0f, 0.5f) : new Vector2(0.5f, 0.5f);
        Vector2 iconJustify = unCentered
            ? new Vector2(ActiveFont.Measure(Label).X + icon.Width, 5f)
            : new Vector2(ActiveFont.Measure(Label).X / 2f + icon.Width, 5f);
        DrawIcon(titlePosition, iconJustify, true, Items.Count < 1 ? Color.DarkSlateGray : color, alpha);
        ActiveFont.DrawOutline(Label, titlePosition, justify, Vector2.One, color, 2f, strokeColor);

        if (Focused) {
            Vector2 menuPosition = new(top.X + ItemIndent, top.Y + TitleHeight + ItemSpacing);
            RecalculateSize();
            foreach (TextMenu.Item item in Items) {
                if (item.Visible) {
                    float height = item.Height();
                    Vector2 itemPosition = menuPosition + new Vector2(0f, height * 0.5f + item.SelectWiggler.Value * 8f);
                    if (itemPosition.Y + height * 0.5f > 0f && itemPosition.Y - height * 0.5f < Engine.Height) {
                        item.Render(itemPosition, Focused && Current == item);
                    }

                    menuPosition.Y += height + ItemSpacing;
                }
            }
        }
    }

    private void DrawIcon(Vector2 position, Vector2 justify, bool outline, Color color, float scale) {
        if (outline) {
            icon.DrawOutlineCentered(position + justify, color, scale);
        } else {
            icon.DrawCentered(position + justify, color, scale);
        }
    }
}
