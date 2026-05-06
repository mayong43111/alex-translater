using System.Text.Json;

namespace Translater_App.Helpers;

public enum AppTheme
{
    System = 0,
    Light = 1,
    Dark = 2
}

public class AppSettings
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Translater");
    private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");

    public uint HotkeyModifiers { get; set; } = HotKeyManager.MOD_ALT;
    public uint HotkeyKey { get; set; } = 0x44; // VK_D
    public AppTheme Theme { get; set; } = AppTheme.System;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFile, json);
        }
        catch { }
    }

    public string GetHotkeyDisplayString()
    {
        var parts = new List<string>();
        if ((HotkeyModifiers & HotKeyManager.MOD_CTRL) != 0) parts.Add("Ctrl");
        if ((HotkeyModifiers & HotKeyManager.MOD_ALT) != 0) parts.Add("Alt");
        if ((HotkeyModifiers & HotKeyManager.MOD_SHIFT) != 0) parts.Add("Shift");
        parts.Add(GetKeyName(HotkeyKey));
        return string.Join("+", parts);
    }

    private static string GetKeyName(uint vk)
    {
        // A-Z
        if (vk >= 0x41 && vk <= 0x5A)
            return ((char)vk).ToString();
        // 0-9
        if (vk >= 0x30 && vk <= 0x39)
            return ((char)vk).ToString();
        // F1-F12
        if (vk >= 0x70 && vk <= 0x7B)
            return $"F{vk - 0x70 + 1}";
        return $"0x{vk:X2}";
    }
}
