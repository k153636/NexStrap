using Avalonia.Input;

namespace NexStrap.Models;

public record HotKeyBinding(int Modifiers, int VirtualKey)
{
    // Modifiers bits: 1=Alt, 2=Ctrl, 4=Shift
    public static readonly HotKeyBinding Empty = new(0, 0);
    public bool IsEmpty => VirtualKey == 0;

    public static HotKeyBinding FromAvaloniaKey(KeyModifiers mods, Key key)
    {
        var vk = ToVirtualKey(key);
        if (vk == 0) return Empty;

        int m = 0;
        if (mods.HasFlag(KeyModifiers.Control)) m |= 2;
        if (mods.HasFlag(KeyModifiers.Shift))   m |= 4;
        if (mods.HasFlag(KeyModifiers.Alt))     m |= 1;
        return new HotKeyBinding(m, vk);
    }

    public static HotKeyBinding Parse(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return Empty;
        var parts = s.Split('+');
        int mods = 0;
        int vk   = 0;
        foreach (var p in parts)
        {
            switch (p.Trim().ToLowerInvariant())
            {
                case "ctrl":  mods |= 2; break;
                case "shift": mods |= 4; break;
                case "alt":   mods |= 1; break;
                default:
                    vk = NameToVk(p.Trim());
                    break;
            }
        }
        return vk == 0 ? Empty : new HotKeyBinding(mods, vk);
    }

    public override string ToString()
    {
        if (IsEmpty) return "Not set";
        var parts = new System.Collections.Generic.List<string>();
        if ((Modifiers & 2) != 0) parts.Add("Ctrl");
        if ((Modifiers & 4) != 0) parts.Add("Shift");
        if ((Modifiers & 1) != 0) parts.Add("Alt");
        parts.Add(VkToName(VirtualKey));
        return string.Join("+", parts);
    }

    private static int ToVirtualKey(Key key) => key switch
    {
        Key.F1  => 0x70, Key.F2  => 0x71, Key.F3  => 0x72, Key.F4  => 0x73,
        Key.F5  => 0x74, Key.F6  => 0x75, Key.F7  => 0x76, Key.F8  => 0x77,
        Key.F9  => 0x78, Key.F10 => 0x79, Key.F11 => 0x7A, Key.F12 => 0x7B,
        Key.A => 0x41, Key.B => 0x42, Key.C => 0x43, Key.D => 0x44,
        Key.E => 0x45, Key.F => 0x46, Key.G => 0x47, Key.H => 0x48,
        Key.I => 0x49, Key.J => 0x4A, Key.K => 0x4B, Key.L => 0x4C,
        Key.M => 0x4D, Key.N => 0x4E, Key.O => 0x4F, Key.P => 0x50,
        Key.Q => 0x51, Key.R => 0x52, Key.S => 0x53, Key.T => 0x54,
        Key.U => 0x55, Key.V => 0x56, Key.W => 0x57, Key.X => 0x58,
        Key.Y => 0x59, Key.Z => 0x5A,
        Key.D0 => 0x30, Key.D1 => 0x31, Key.D2 => 0x32, Key.D3 => 0x33,
        Key.D4 => 0x34, Key.D5 => 0x35, Key.D6 => 0x36, Key.D7 => 0x37,
        Key.D8 => 0x38, Key.D9 => 0x39,
        Key.NumPad0 => 0x60, Key.NumPad1 => 0x61, Key.NumPad2 => 0x62,
        Key.NumPad3 => 0x63, Key.NumPad4 => 0x64, Key.NumPad5 => 0x65,
        Key.NumPad6 => 0x66, Key.NumPad7 => 0x67, Key.NumPad8 => 0x68,
        Key.NumPad9 => 0x69,
        Key.Insert => 0x2D, Key.Delete => 0x2E,
        Key.Home   => 0x24, Key.End    => 0x23,
        Key.Prior  => 0x21, Key.Next   => 0x22,
        Key.Left   => 0x25, Key.Up     => 0x26, Key.Right => 0x27, Key.Down => 0x28,
        Key.OemSemicolon  => 0xBA, Key.OemPlus    => 0xBB, Key.OemComma  => 0xBC,
        Key.OemMinus      => 0xBD, Key.OemPeriod  => 0xBE, Key.OemQuestion => 0xBF,
        Key.OemTilde      => 0xC0, Key.OemOpenBrackets => 0xDB,
        Key.OemCloseBrackets => 0xDD, Key.OemQuotes => 0xDE,
        _ => 0
    };

    private static int NameToVk(string name) => name.ToUpperInvariant() switch
    {
        "F1"  => 0x70, "F2"  => 0x71, "F3"  => 0x72, "F4"  => 0x73,
        "F5"  => 0x74, "F6"  => 0x75, "F7"  => 0x76, "F8"  => 0x77,
        "F9"  => 0x78, "F10" => 0x79, "F11" => 0x7A, "F12" => 0x7B,
        "A" => 0x41, "B" => 0x42, "C" => 0x43, "D" => 0x44,
        "E" => 0x45, "F" => 0x46, "G" => 0x47, "H" => 0x48,
        "I" => 0x49, "J" => 0x4A, "K" => 0x4B, "L" => 0x4C,
        "M" => 0x4D, "N" => 0x4E, "O" => 0x4F, "P" => 0x50,
        "Q" => 0x51, "R" => 0x52, "S" => 0x53, "T" => 0x54,
        "U" => 0x55, "V" => 0x56, "W" => 0x57, "X" => 0x58,
        "Y" => 0x59, "Z" => 0x5A,
        "0" => 0x30, "1" => 0x31, "2" => 0x32, "3" => 0x33,
        "4" => 0x34, "5" => 0x35, "6" => 0x36, "7" => 0x37,
        "8" => 0x38, "9" => 0x39,
        "INSERT" => 0x2D, "DELETE" => 0x2E, "DEL" => 0x2E,
        "HOME"   => 0x24, "END"    => 0x23,
        "PAGEUP" => 0x21, "PAGEDOWN" => 0x22,
        _ => 0
    };

    private static string VkToName(int vk) => vk switch
    {
        0x70 => "F1",  0x71 => "F2",  0x72 => "F3",  0x73 => "F4",
        0x74 => "F5",  0x75 => "F6",  0x76 => "F7",  0x77 => "F8",
        0x78 => "F9",  0x79 => "F10", 0x7A => "F11", 0x7B => "F12",
        0x41 => "A",  0x42 => "B",  0x43 => "C",  0x44 => "D",
        0x45 => "E",  0x46 => "F",  0x47 => "G",  0x48 => "H",
        0x49 => "I",  0x4A => "J",  0x4B => "K",  0x4C => "L",
        0x4D => "M",  0x4E => "N",  0x4F => "O",  0x50 => "P",
        0x51 => "Q",  0x52 => "R",  0x53 => "S",  0x54 => "T",
        0x55 => "U",  0x56 => "V",  0x57 => "W",  0x58 => "X",
        0x59 => "Y",  0x5A => "Z",
        0x30 => "0",  0x31 => "1",  0x32 => "2",  0x33 => "3",
        0x34 => "4",  0x35 => "5",  0x36 => "6",  0x37 => "7",
        0x38 => "8",  0x39 => "9",
        0x2D => "Insert", 0x2E => "Delete",
        0x24 => "Home",   0x23 => "End",
        0x21 => "PageUp", 0x22 => "PageDown",
        0x25 => "Left",   0x26 => "Up",   0x27 => "Right",  0x28 => "Down",
        _ => $"Key{vk:X2}"
    };
}
