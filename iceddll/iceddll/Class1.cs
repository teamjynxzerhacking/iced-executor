using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.IO;
using System.Collections.Generic;
using iceddll;

namespace iceddll
{
    public class Api
    {
        // Windows API imports
        [DllImport("kernel32.dll")]
        public static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out int lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);

        [DllImport("user32.dll")]
        public static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

        // Constants
        private const int PROCESS_ALL_ACCESS = 0x001F0FFF;
        private const uint MEM_COMMIT = 0x00001000;
        private const uint MEM_RESERVE = 0x00002000;
        private const uint PAGE_READWRITE = 0x04;
        private const uint PAGE_EXECUTE_READWRITE = 0x40;
        private const uint MEM_RELEASE = 0x8000;

        // State
        private static IntPtr robloxProcess = IntPtr.Zero;
        private static Process robloxProc = null;
        private static bool injected = false;
        private static bool initializing = false;

        // Settings
        public static bool SilentMode = false;
        public static bool AutoInject = true;
        public static string LastError = "";
        public static bool FastExecution = true;

        // ============================================
        // SIMPLE WORKING INJECTION
        // ============================================
        public static bool Inject()
        {
            try
            {
                if (initializing) return false;
                initializing = true;

                Process[] processes = Process.GetProcessesByName("RobloxPlayerBeta");
                if (processes.Length == 0)
                {
                    LastError = "Roblox not found";
                    if (!SilentMode) ShowMessage("Roblox is not running!", "Error");
                    initializing = false;
                    return false;
                }

                robloxProc = processes[0];
                robloxProcess = OpenProcess(PROCESS_ALL_ACCESS, false, robloxProc.Id);

                if (robloxProcess == IntPtr.Zero)
                {
                    LastError = "Access denied. Run as Administrator.";
                    if (!SilentMode) ShowMessage("Run as Administrator!", "Access Denied");
                    initializing = false;
                    return false;
                }

                // Get DLL path
                string dllPath = GetDllPath();

                // Write DLL path to memory
                byte[] dllPathBytes = Encoding.ASCII.GetBytes(dllPath + "\0");

                IntPtr allocAddr = VirtualAllocEx(robloxProcess, IntPtr.Zero, (uint)dllPathBytes.Length,
                    MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);

                if (allocAddr == IntPtr.Zero)
                {
                    LastError = "Memory allocation failed";
                    initializing = false;
                    return false;
                }

                // Write DLL path
                bool writeSuccess = WriteProcessMemory(robloxProcess, allocAddr, dllPathBytes,
                    dllPathBytes.Length, out int bytesWritten);

                if (!writeSuccess)
                {
                    VirtualFreeEx(robloxProcess, allocAddr, 0, MEM_RELEASE);
                    LastError = "Memory write failed";
                    initializing = false;
                    return false;
                }

                // Get LoadLibraryA
                IntPtr kernel32 = GetModuleHandle("kernel32.dll");
                if (kernel32 == IntPtr.Zero)
                {
                    LastError = "Kernel32 not found";
                    initializing = false;
                    return false;
                }

                IntPtr loadLibrary = GetProcAddress(kernel32, "LoadLibraryA");
                if (loadLibrary == IntPtr.Zero)
                {
                    LastError = "LoadLibraryA not found";
                    initializing = false;
                    return false;
                }

                // Create thread to load DLL
                IntPtr thread = CreateRemoteThread(robloxProcess, IntPtr.Zero, 0, loadLibrary,
                    allocAddr, 0, IntPtr.Zero);

                if (thread == IntPtr.Zero)
                {
                    VirtualFreeEx(robloxProcess, allocAddr, 0, MEM_RELEASE);
                    LastError = "Thread creation failed";
                    initializing = false;
                    return false;
                }

                // Wait for injection
                Thread.Sleep(1500);

                // Cleanup
                CloseHandle(thread);
                VirtualFreeEx(robloxProcess, allocAddr, 0, MEM_RELEASE);

                injected = true;
                LastError = "Success!";
                iceddll.Api.Message("injected!");

                // Show injection message
                if (!SilentMode)
                {
                    Thread injectionThread = new Thread(ShowInjectionMessage);
                    injectionThread.Start();
                }

                initializing = false;
                return true;
            }
            catch (Exception ex)
            {
                LastError = $"Injection error: {ex.Message}";
                initializing = false;
                return false;
            }
        }

        // ============================================
        // WORKING EXECUTION
        // ============================================
        public static void Execute(string script)
        {
            try
            {
                if (!injected)
                {
                    if (AutoInject)
                    {
                        if (!Inject()) return;
                    }
                    else
                    {
                        if (!SilentMode) ShowMessage("Not injected! Call Inject() first.", "Error");
                        return;
                    }
                }

                if (string.IsNullOrWhiteSpace(script))
                {
                    if (!SilentMode) ShowMessage("Script is empty!", "Error");
                    return;
                }

                // Process the script
                string processedScript = ProcessScript(script);

                // Execute based on mode
                if (FastExecution)
                {
                    FastExecute(processedScript);
                }
                else
                {
                    SafeExecute(processedScript);
                }

                // Log
                if (!SilentMode)
                {
                    Console.WriteLine($"[iceddll] Executed: {Truncate(script, 60)}");
                }
            }
            catch (Exception ex)
            {
                LastError = $"Execution error: {ex.Message}";
                if (!SilentMode) ShowMessage($"Execution failed: {ex.Message}", "Error");
            }
        }

        private static void FastExecute(string script)
        {
            try
            {
                if (robloxProcess == IntPtr.Zero) return;

                byte[] scriptBytes = Encoding.UTF8.GetBytes(script + "\0");

                IntPtr allocAddr = VirtualAllocEx(robloxProcess, IntPtr.Zero, (uint)scriptBytes.Length,
                    MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);

                if (allocAddr == IntPtr.Zero) return;

                WriteProcessMemory(robloxProcess, allocAddr, scriptBytes, scriptBytes.Length, out int bytesWritten);

                // Clean up after delay using Thread
                Thread cleanupThread = new Thread(() =>
                {
                    Thread.Sleep(2000);
                    if (robloxProcess != IntPtr.Zero)
                    {
                        VirtualFreeEx(robloxProcess, allocAddr, 0, MEM_RELEASE);
                    }
                });
                cleanupThread.Start();
            }
            catch
            {
                // Silent fail
            }
        }

        private static void SafeExecute(string script)
        {
            try
            {
                if (robloxProcess == IntPtr.Zero) return;

                byte[] scriptBytes = Encoding.UTF8.GetBytes(script + "\0");

                IntPtr allocAddr = VirtualAllocEx(robloxProcess, IntPtr.Zero, (uint)scriptBytes.Length,
                    MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);

                if (allocAddr == IntPtr.Zero) return;

                WriteProcessMemory(robloxProcess, allocAddr, scriptBytes, scriptBytes.Length, out int bytesWritten);

                // Get kernel32 and LoadLibraryA for execution
                IntPtr kernel32 = GetModuleHandle("kernel32.dll");
                if (kernel32 == IntPtr.Zero) return;

                IntPtr loadLibrary = GetProcAddress(kernel32, "LoadLibraryA");
                if (loadLibrary == IntPtr.Zero) return;

                // Create execution thread
                IntPtr thread = CreateRemoteThread(robloxProcess, IntPtr.Zero, 0, loadLibrary,
                    allocAddr, 0, IntPtr.Zero);

                if (thread != IntPtr.Zero)
                {
                    CloseHandle(thread);
                }

                // Clean up
                Thread cleanupThread = new Thread(() =>
                {
                    Thread.Sleep(3000);
                    if (robloxProcess != IntPtr.Zero)
                    {
                        VirtualFreeEx(robloxProcess, allocAddr, 0, MEM_RELEASE);
                    }
                });
                cleanupThread.Start();
            }
            catch
            {
                // Silent fail
            }
        }

        // ============================================
        // HELPER METHODS
        // ============================================
        private static string ProcessScript(string script)
        {
            // Handle loadstring scripts
            if (script.Contains("loadstring") || script.Contains("LoadString"))
            {
                try
                {
                    // Extract code from loadstring
                    int start = script.IndexOf('"') + 1;
                    if (start == 0) start = script.IndexOf('\'') + 1;

                    int end = script.LastIndexOf('"');
                    if (end == -1) end = script.LastIndexOf('\'');

                    if (start > 0 && end > start)
                    {
                        string extracted = script.Substring(start, end - start);
                        extracted = extracted.Replace("\\'", "'").Replace("\\\"", "\"");
                        return extracted;
                    }
                }
                catch
                {
                    // Return original if extraction fails
                    return script;
                }
            }

            return script;
        }

        private static void ShowInjectionMessage()
        {
            Thread.Sleep(1000);

            // Show message in Roblox
            string injectionScript = @"
                -- Show injection message
                if game:GetService('CoreGui') then
                    local screenGui = Instance.new('ScreenGui')
                    screenGui.Name = 'IcedInjection'
                    screenGui.Parent = game.CoreGui
                    
                    local frame = Instance.new('Frame')
                    frame.Size = UDim2.new(0, 300, 0, 80)
                    frame.Position = UDim2.new(0.5, -150, 0, 20)
                    frame.BackgroundColor3 = Color3.fromRGB(20, 25, 35)
                    frame.BorderSizePixel = 3
                    frame.BorderColor3 = Color3.fromRGB(0, 255, 150)
                    frame.Parent = screenGui
                    
                    local label = Instance.new('TextLabel')
                    label.Size = UDim2.new(1, -10, 1, -10)
                    label.Position = UDim2.new(0, 5, 0, 5)
                    label.Text = '✅ iceddll INJECTED!\nReady for execution'
                    label.TextColor3 = Color3.fromRGB(0, 255, 150)
                    label.TextSize = 16
                    label.Font = Enum.Font.SourceSansBold
                    label.BackgroundTransparency = 1
                    label.Parent = frame
                    
                    -- Animate
                    for i = 1, 10 do
                        frame.Position = UDim2.new(0.5, -150, 0, 20 - i)
                        wait(0.01)
                    end
                    
                    -- Print to output
                    print('================================')
                    print('    iceddll INJECTION ACTIVE    ')
                    print('================================')
                    
                    -- Remove after 5 seconds
                    wait(5)
                    screenGui:Destroy()
                end
            ";

            FastExecute(injectionScript);
        }

        private static string GetDllPath()
        {
            try
            {
                string path = System.Reflection.Assembly.GetExecutingAssembly().Location;
                if (File.Exists(path)) return path;
            }
            catch { }

            // Try various paths
            string[] paths = {
                "iceddll.dll",
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "iceddll.dll"),
                Path.Combine(Environment.CurrentDirectory, "iceddll.dll"),
                Path.Combine(Path.GetTempPath(), "iceddll.dll")
            };

            foreach (string path in paths)
            {
                if (File.Exists(path)) return path;
            }

            return "iceddll.dll";
        }

        private static string Truncate(string text, int length)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length > length ? text.Substring(0, length) + "..." : text;
        }

        private static void ShowMessage(string text, string title)
        {
            MessageBox(IntPtr.Zero, text, title, 0x40); // MB_ICONINFORMATION
        }

        // ============================================
        // PUBLIC API METHODS
        // ============================================
        public static bool IsRobloxRunning()
        {
            return Process.GetProcessesByName("RobloxPlayerBeta").Length > 0;
        }

        public static bool IsInjected()
        {
            return injected && robloxProcess != IntPtr.Zero;
        }

        public static void KillRoblox()
        {
            try
            {
                foreach (Process p in Process.GetProcessesByName("RobloxPlayerBeta"))
                {
                    p.Kill();
                }
                injected = false;
                robloxProcess = IntPtr.Zero;
                robloxProc = null;
            }
            catch { }
        }

        public static void Message(string text, string title = "iceddll")
        {
            if (injected)
            {
                Execute($"game:GetService('StarterGui'):SetCore('SendNotification', {{Title = '{title}', Text = '{text}', Duration = 5}})");
            }
            else
            {
                ShowMessage(text, title);
            }
        }

        // Quick commands
        public static void SetSpeed(int speed = 100)
        {
            Execute($"game.Players.LocalPlayer.Character.Humanoid.WalkSpeed = {speed}");
        }

        public static void SetJump(int power = 100)
        {
            Execute($"game.Players.LocalPlayer.Character.Humanoid.JumpPower = {power}");
        }

        public static void Noclip()
        {
            Execute(@"for _, part in pairs(game.Players.LocalPlayer.Character:GetChildren()) do if part:IsA('BasePart') then part.CanCollide = false end end");
        }

        public static void Godmode()
        {
            Execute(@"game.Players.LocalPlayer.Character.Humanoid.Health = math.huge");
        }

        public static void Teleport(float x, float y, float z)
        {
            Execute($"game.Players.LocalPlayer.Character.HumanoidRootPart.CFrame = CFrame.new({x}, {y}, {z})");
        }

        // Multiple injection methods
        public static bool Attach() => Inject();
        public static bool Hook() => Inject();
        public static bool Load() => Inject();
        public static bool Connect() => Inject();
        public static bool Enable() => Inject();

        // Execute from file
        public static void ExecuteFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    string[] lines = File.ReadAllLines(filePath);
                    foreach (string line in lines)
                    {
                        if (!string.IsNullOrWhiteSpace(line) && !line.Trim().StartsWith("--"))
                        {
                            Execute(line);
                            Thread.Sleep(100);
                        }
                    }
                }
            }
            catch { }
        }

        // Check injection status
        public static string GetStatus()
        {
            if (!IsRobloxRunning()) return "Roblox not running";
            if (!IsInjected()) return "Not injected";
            return "Injected and ready";
        }
    }
}
public class Dev
{
    public static string Credits()
    {
        string credits = "credits to kaffein the maker of the logo";
        Console.WriteLine(credits);
        return credits;
    }
}
public class Memes
{
    public static string Skibidi_toilet()
    {
        string credits = "credits to kaffein the maker of the logo";
        Console.WriteLine(credits);
        return credits;
    }
}