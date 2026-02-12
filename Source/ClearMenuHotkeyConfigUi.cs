using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;

namespace Celeste.Mod.ClearMenu;

[Tracked]
public class ClearMenuHotkeyConfigUi : TextMenu {
    private readonly List<HotkeyEntry> entries;
    private bool closing;
    private bool remapping;
    private float remappingEase;
    private float timeout;
    private float inputDelay;
    private int firstHotkeyIndex;
    private HotkeyEntry remappingEntry;

    private static readonly HashSet<Keys> DisallowKeys = new() {
        Keys.None,
        Keys.LeftWindows,
        Keys.RightWindows
    };

    public ClearMenuHotkeyConfigUi() {
        entries = new List<HotkeyEntry> {
            new("ClearMenu_Setting_HotkeyClimb", () => ClearMenuModule.Settings.HotkeyClimb, value => ClearMenuModule.Settings.HotkeyClimb = value),
            new("ClearMenu_Setting_HotkeyPico8", () => ClearMenuModule.Settings.HotkeyPico8, value => ClearMenuModule.Settings.HotkeyPico8 = value),
            new("ClearMenu_Setting_HotkeyOptions", () => ClearMenuModule.Settings.HotkeyOptions, value => ClearMenuModule.Settings.HotkeyOptions = value),
            new("ClearMenu_Setting_HotkeyModOptions", () => ClearMenuModule.Settings.HotkeyModOptions, value => ClearMenuModule.Settings.HotkeyModOptions = value),
            new("ClearMenu_Setting_HotkeyCredits", () => ClearMenuModule.Settings.HotkeyCredits, value => ClearMenuModule.Settings.HotkeyCredits = value),
            new("ClearMenu_Setting_HotkeyExit", () => ClearMenuModule.Settings.HotkeyExit, value => ClearMenuModule.Settings.HotkeyExit = value)
        };

        Reload();
        OnESC = OnCancel = () => {
            Focused = false;
            closing = true;
        };
        MinWidth = 600f;
        Position.Y = ScrollTargetY;
        Alpha = 0f;
    }

    private void Reload(int selection = -1) {
        Clear();

        Add(new Header(Dialog.Clean("ClearMenu_HotkeyConfig_Title")));
        Add(new SubHeader(Dialog.Clean("ClearMenu_HotkeyConfig_PressDelete")));
        Add(new SubHeader(Dialog.Clean("ClearMenu_HotkeyConfig_Keyboard")));

        firstHotkeyIndex = Items.Count;
        foreach (HotkeyEntry entry in entries) {
            AddKeyboardSetting(entry);
        }

        Add(new SubHeader(""));
        Add(new Button(Dialog.Clean("ClearMenu_HotkeyConfig_Reset")).Pressed(() => {
            ResetDefaults();
            Reload(Selection);
        }));

        if (selection >= 0) {
            Selection = Math.Min(selection, Items.Count - 1);
        }
    }

    private static void ResetDefaults() {
        ClearMenuModule.Settings.HotkeyClimb = Keys.None;
        ClearMenuModule.Settings.HotkeyPico8 = Keys.None;
        ClearMenuModule.Settings.HotkeyOptions = Keys.D1;
        ClearMenuModule.Settings.HotkeyModOptions = Keys.D2;
        ClearMenuModule.Settings.HotkeyCredits = Keys.None;
        ClearMenuModule.Settings.HotkeyExit = Keys.None;
    }

    private static string FormatKey(Keys key) {
        if (key == Keys.None) {
            return Dialog.Clean("ClearMenu_Hotkey_None");
        }
        return key.ToString().ToUpperInvariant();
    }

    private void BeginRemap(HotkeyEntry entry) {
        remapping = true;
        remappingEntry = entry;
        timeout = 5f;
        Focused = false;
    }

    private void SetRemap(Keys key) {
        remapping = false;
        inputDelay = 0.2f;
        remappingEntry.Set(key);
        Reload(Selection);
    }

    public override void Update() {
        base.Update();

        if (inputDelay > 0f && !remapping) {
            inputDelay -= Engine.DeltaTime;
            if (inputDelay <= 0f) {
                Focused = true;
            }
        }

        remappingEase = Calc.Approach(remappingEase, remapping ? 1f : 0f, Engine.DeltaTime * 4f);

        if (remappingEase > 0.5f && remapping) {
            if (Input.ESC.Pressed || Input.MenuCancel || timeout <= 0f) {
                Input.ESC.ConsumePress();
                remapping = false;
                Focused = true;
            } else if (MInput.Keyboard.Pressed(Keys.Delete) || MInput.Keyboard.Pressed(Keys.Back) || Input.MenuJournal.Pressed) {
                SetRemap(Keys.None);
            } else {
                Keys[] pressedKeys = MInput.Keyboard.CurrentState.GetPressedKeys();
                Keys pressedKey = pressedKeys?.Length > 0 ? pressedKeys[^1] : Keys.None;
                if (pressedKey != Keys.None && MInput.Keyboard.Pressed(pressedKey) && !DisallowKeys.Contains(pressedKey)) {
                    SetRemap(pressedKey);
                }
            }

            timeout -= Engine.DeltaTime;
        } else if ((Input.MenuJournal.Pressed || MInput.Keyboard.Pressed(Keys.Delete) || MInput.Keyboard.Pressed(Keys.Back)) &&
                   Selection >= firstHotkeyIndex && Selection < firstHotkeyIndex + entries.Count) {
            entries[Selection - firstHotkeyIndex].Set(Keys.None);
            Reload(Selection);
        }

        Alpha = Calc.Approach(Alpha, closing ? 0f : 1f, Engine.DeltaTime * 8f);
        if (closing && Alpha <= 0f) {
            RemoveSelf();
            OnClose?.Invoke();
        }
    }

    public override void Render() {
        Draw.Rect(-10f, -10f, 1940f, 1100f, Color.Black * Ease.CubeOut(Alpha));
        base.Render();

        if (remappingEase <= 0f) {
            return;
        }

        Draw.Rect(-10f, -10f, 1940f, 1100f, Color.Black * 0.95f * Ease.CubeInOut(remappingEase));
        Vector2 center = new Vector2(1920f, 1080f) * 0.5f;
        ActiveFont.Draw(
            Dialog.Clean("ClearMenu_HotkeyConfig_RemapHint"),
            center + new Vector2(0f, -52f),
            new Vector2(0.5f, 2f),
            Vector2.One * 0.7f,
            Color.LightGray * Ease.CubeIn(remappingEase)
        );
        ActiveFont.Draw(
            Dialog.Clean("ClearMenu_HotkeyConfig_RemapCancel"),
            center + new Vector2(0f, -32f),
            new Vector2(0.5f, 2f),
            Vector2.One * 0.7f,
            Color.LightGray * Ease.CubeIn(remappingEase)
        );
        ActiveFont.Draw(
            Dialog.Clean("ClearMenu_HotkeyConfig_RemapPrompt"),
            center + new Vector2(0f, -8f),
            new Vector2(0.5f, 1f),
            Vector2.One * 0.7f,
            Color.LightGray * Ease.CubeIn(remappingEase)
        );
        ActiveFont.Draw(
            Dialog.Clean(remappingEntry.LabelKey),
            center + new Vector2(0f, 8f),
            new Vector2(0.5f, 0f),
            Vector2.One * 2f,
            Color.White * Ease.CubeIn(remappingEase)
        );
    }

    private void AddKeyboardSetting(HotkeyEntry entry) {
        List<Keys> keys = entry.Get() == Keys.None ? new List<Keys>() : new List<Keys> { entry.Get() };
        Add(new Setting(Dialog.Clean(entry.LabelKey), keys).Pressed(() => BeginRemap(entry)));
    }

    private readonly struct HotkeyEntry {
        public readonly string LabelKey;
        private readonly Func<Keys> getter;
        private readonly Action<Keys> setter;

        public HotkeyEntry(string labelKey, Func<Keys> getter, Action<Keys> setter) {
            LabelKey = labelKey;
            this.getter = getter;
            this.setter = setter;
        }

        public Keys Get() => getter();
        public void Set(Keys key) => setter(key);
    }
}
