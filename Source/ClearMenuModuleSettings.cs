using Celeste.Mod;
using Microsoft.Xna.Framework.Input;

namespace Celeste.Mod.ClearMenu;

public class ClearMenuModuleSettings : EverestModuleSettings {
    [SettingName("ClearMenu_Setting_Enabled")]
    public bool Enabled {
        get => _enabled;
        set {
            if (_enabled == value) {
                return;
            }
            bool enabling = !_enabled && value;
            _enabled = value;
            ClearMenuModule.RequestMainMenuRebuild();
            if (enabling) {
                ClearMenuModule.RequestSelectFirstButton();
            }
        }
    }

    private bool _enabled = true;

    [SettingIgnore]
    public bool HidePico8 { get; set; } = true;

    [SettingIgnore]
    public bool HideOptions { get; set; } = true;

    [SettingIgnore]
    public bool HideModOptions { get; set; } = true;

    [SettingIgnore]
    public bool HideCredits { get; set; } = true;

    [SettingIgnore]
    public bool HideExit { get; set; } = true;

    [SettingIgnore]
    public bool HideButtonTips { get; set; } = true;

    [SettingIgnore]
    public Keys HotkeyClimb { get; set; } = Keys.None;

    [SettingIgnore]
    public Keys HotkeyPico8 { get; set; } = Keys.None;

    [SettingIgnore]
    public Keys HotkeyOptions { get; set; } = Keys.D1;

    [SettingIgnore]
    public Keys HotkeyModOptions { get; set; } = Keys.D2;

    [SettingIgnore]
    public Keys HotkeyCredits { get; set; } = Keys.None;

    [SettingIgnore]
    public Keys HotkeyExit { get; set; } = Keys.None;
}
