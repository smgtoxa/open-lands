// The browser's file handling on Windows: <input type="file"> is the system "Open" dialog (comdlg32), and a
// download lands in the user's Downloads folder, as Chrome saves it without asking.
// On macOS there is no comdlg32: the same two dialogs come from the system chooser through osascript, which
// every Mac has, so the player needs no native plugin. Other platforms keep returning null as before.
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace LolHost
{
    public static class FileDialog
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct OpenFileName
        {
            public int lStructSize;
            public IntPtr hwndOwner, hInstance;
            public string lpstrFilter;
            public string lpstrCustomFilter;
            public int nMaxCustFilter, nFilterIndex;
            public IntPtr lpstrFile;
            public int nMaxFile;
            public string lpstrFileTitle;
            public int nMaxFileTitle;
            public string lpstrInitialDir, lpstrTitle;
            public int Flags;
            public short nFileOffset, nFileExtension;
            public string lpstrDefExt;
            public IntPtr lCustData, lpfnHook;
            public string lpTemplateName;
            public IntPtr pvReserved;
            public int dwReserved, FlagsEx;
        }

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool GetOpenFileNameW(ref OpenFileName ofn);

        [DllImport("user32.dll")]
        static extern IntPtr GetActiveWindow();

        /// <summary>The system "Open" dialog; null when cancelled (or not on Windows).</summary>
        public static string Open(string title, string filterName, string pattern)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            const int OFN_FILEMUSTEXIST = 0x1000, OFN_PATHMUSTEXIST = 0x800, OFN_NOCHANGEDIR = 0x8, OFN_EXPLORER = 0x80000;
            var buffer = Marshal.AllocHGlobal(4096 * 2);
            try
            {
                Marshal.WriteInt16(buffer, 0);
                var ofn = new OpenFileName
                {
                    lStructSize = Marshal.SizeOf(typeof(OpenFileName)),
                    hwndOwner = GetActiveWindow(),
                    lpstrFilter = $"{filterName}\0{pattern}\0All files\0*.*\0\0",
                    nFilterIndex = 1,
                    lpstrFile = buffer,
                    nMaxFile = 4096,
                    lpstrInitialDir = Downloads(),
                    lpstrTitle = title,
                    Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | OFN_EXPLORER,
                };
                return GetOpenFileNameW(ref ofn) ? Marshal.PtrToStringUni(buffer) : null;
            }
            finally { Marshal.FreeHGlobal(buffer); }
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            // "*.json" is the pattern the page gives; the panel wants the bare extension
            string ext = (pattern ?? "").TrimStart('*', '.');
            string picked = Panel(title, false, ext);
            if (picked != null) return picked.Length == 0 ? null : picked;
            string ofType = ext.Length == 0 ? "" : $" of type {{\"{Quoted(ext)}\"}}";
            return Osa($"POSIX path of (choose file with prompt \"{Quoted(title)}\"{ofType})");
#else
            return null;
#endif
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct BrowseInfo
        {
            public IntPtr hwndOwner, pidlRoot;
            public IntPtr pszDisplayName;
            public string lpszTitle;
            public uint ulFlags;
            public IntPtr lpfn, lParam;
            public int iImage;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SHBrowseForFolderW(ref BrowseInfo bi);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern bool SHGetPathFromIDListW(IntPtr pidl, IntPtr path);

        [DllImport("ole32.dll")]
        static extern void CoTaskMemFree(IntPtr pv);

        /// <summary>The system "choose a folder" dialog (dialog.showOpenDialog openDirectory); null when cancelled.</summary>
        public static string PickFolder(string title)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            const uint BIF_RETURNONLYFSDIRS = 0x1, BIF_NEWDIALOGSTYLE = 0x40, BIF_NONEWFOLDERBUTTON = 0x200;
            var name = Marshal.AllocHGlobal(260 * 2);
            var path = Marshal.AllocHGlobal(1024 * 2);
            try
            {
                var bi = new BrowseInfo { hwndOwner = GetActiveWindow(), pszDisplayName = name, lpszTitle = title, ulFlags = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE | BIF_NONEWFOLDERBUTTON };
                var pidl = SHBrowseForFolderW(ref bi);
                if (pidl == IntPtr.Zero) return null;
                try { return SHGetPathFromIDListW(pidl, path) ? Marshal.PtrToStringUni(path) : null; }
                finally { CoTaskMemFree(pidl); }
            }
            finally { Marshal.FreeHGlobal(name); Marshal.FreeHGlobal(path); }
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            string picked = Panel(title, true, null);
            if (picked != null) return picked.Length == 0 ? null : picked;
            return Osa($"POSIX path of (choose folder with prompt \"{Quoted(title)}\")");
#else
            return null;
#endif
        }

#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        // AppKit's own chooser, asked for through the Objective-C runtime. It runs inside the player, so it
        // belongs to the game's window and opens on the game's Space; osascript's dialog is another process and
        // macOS leaves it on the desktop the game came from, out of sight whenever the game is full screen.
        const string ObjC = "/usr/lib/libobjc.dylib";

        [DllImport(ObjC, EntryPoint = "objc_getClass", CharSet = CharSet.Ansi)]
        static extern IntPtr Class(string name);

        [DllImport(ObjC, EntryPoint = "sel_registerName", CharSet = CharSet.Ansi)]
        static extern IntPtr Sel(string name);

        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        static extern IntPtr Send(IntPtr self, IntPtr op);

        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        static extern IntPtr Send(IntPtr self, IntPtr op, IntPtr a);

        // BOOL is one byte: a C# bool would go over as four and the panel would read a stray flag
        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        static extern void SendB(IntPtr self, IntPtr op, byte a);

        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        static extern long SendI(IntPtr self, IntPtr op);

        static IntPtr Str(string text)
        {
            IntPtr utf8 = Marshal.StringToHGlobalAnsi(text ?? "");
            try { return Send(Class("NSString"), Sel("stringWithUTF8String:"), utf8); }
            finally { Marshal.FreeHGlobal(utf8); }
        }

        /// <summary>NSOpenPanel: the chosen path, "" when cancelled, null when AppKit could not be asked
        /// (the caller then falls back to osascript).</summary>
        static string Panel(string title, bool folders, string ext)
        {
            try
            {
                IntPtr cls = Class("NSOpenPanel");
                if (cls == IntPtr.Zero) return null;
                IntPtr panel = Send(cls, Sel("openPanel"));
                if (panel == IntPtr.Zero) return null;
                SendB(panel, Sel("setCanChooseFiles:"), (byte)(folders ? 0 : 1));
                SendB(panel, Sel("setCanChooseDirectories:"), (byte)(folders ? 1 : 0));
                SendB(panel, Sel("setAllowsMultipleSelection:"), 0);
                SendB(panel, Sel("setCanCreateDirectories:"), 0);
                Send(panel, Sel("setMessage:"), Str(title));
                if (!folders && !string.IsNullOrEmpty(ext))
                {
                    IntPtr one = Send(Class("NSArray"), Sel("arrayWithObject:"), Str(ext));
                    if (one != IntPtr.Zero) Send(panel, Sel("setAllowedFileTypes:"), one);
                }
                // NSModalResponseOK; anything else is a cancel, which is a plain "nothing chosen"
                if (SendI(panel, Sel("runModal")) != 1) return "";
                IntPtr urls = Send(panel, Sel("URLs"));
                if (urls == IntPtr.Zero || SendI(urls, Sel("count")) < 1) return "";
                IntPtr url = Send(urls, Sel("firstObject"));
                if (url == IntPtr.Zero) return "";
                IntPtr path = Send(url, Sel("path"));
                if (path == IntPtr.Zero) return "";
                IntPtr utf8 = Send(path, Sel("UTF8String"));
                return utf8 == IntPtr.Zero ? "" : (Marshal.PtrToStringAnsi(utf8) ?? "");
            }
            catch (Exception) { return null; }   // no AppKit here: osascript still answers
        }

        /// <summary>A string as AppleScript spells it, inside its quotes.</summary>
        static string Quoted(string text) => (text ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

        /// <summary>osascript's answer; null when the user cancelled (it leaves with 1) or it could not run.
        /// The script goes through a file, so no quoting of ours has to survive the command line.</summary>
        static string Osa(string script)
        {
            string file = Path.Combine(Path.GetTempPath(), "open-lands-chooser.applescript");
            try
            {
                File.WriteAllText(file, script);
                var start = new System.Diagnostics.ProcessStartInfo("/usr/bin/osascript", $"\"{file}\"")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using (var osa = System.Diagnostics.Process.Start(start))
                {
                    string chosen = osa.StandardOutput.ReadToEnd();
                    osa.StandardError.ReadToEnd();
                    osa.WaitForExit();
                    if (osa.ExitCode != 0) return null;
                    chosen = chosen.Trim();
                    if (chosen.Length == 0) return null;
                    // a folder comes back with a trailing slash; the rest of the host expects a plain path
                    return chosen.Length > 1 && chosen.EndsWith("/") ? chosen.Substring(0, chosen.Length - 1) : chosen;
                }
            }
            catch (Exception) { return null; }
            finally { try { File.Delete(file); } catch (Exception) { /* a leftover in temp is harmless */ } }
        }
#endif

        /// <summary>The user's Downloads folder (where a browser saves a download).</summary>
        public static string Downloads()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            return Directory.Exists(dir) ? dir : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        /// <summary>a.download = name; a.click(): the file in Downloads, "name (1).ext" when it exists already.</summary>
        public static string Download(string name, byte[] bytes)
        {
            string dir = Downloads();
            string path = Path.Combine(dir, name);
            string stem = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name);
            for (int n = 1; File.Exists(path); n += 1) path = Path.Combine(dir, $"{stem} ({n}){ext}");
            File.WriteAllBytes(path, bytes);
            return path;
        }
    }
}
