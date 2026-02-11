using Celeste.Mod;

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
}
