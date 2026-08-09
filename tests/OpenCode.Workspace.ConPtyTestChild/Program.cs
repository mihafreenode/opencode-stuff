using System.Runtime.InteropServices;
using System.Text;

var input = CreateFile("CONIN$", 0x80000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
var output = CreateFile("CONOUT$", 0x40000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
Write(output, "READY\r\n"u8.ToArray());
var buffer = new byte[256];
var line = new List<byte>();
while (ReadFile(input, buffer, buffer.Length, out var count, IntPtr.Zero))
{
    for (var index = 0; index < count; index++)
    {
        var value = buffer[index];
        if (value != '\n')
        {
            if (value != '\r') line.Add(value);
            continue;
        }
        var text = Encoding.UTF8.GetString(line.ToArray());
        line.Clear();
        if (text == "EXIT") return;
        Write(output, Encoding.UTF8.GetBytes($"ECHO:{text}\r\n"));
    }
}

static void Write(IntPtr handle, byte[] bytes)
{
    var text = Encoding.UTF8.GetString(bytes);
    if (!WriteConsole(handle, text, text.Length, out var written, IntPtr.Zero) || written != text.Length)
        Environment.Exit(Marshal.GetLastWin32Error());
}

[DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
static extern IntPtr CreateFile(string fileName, uint access, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flags, IntPtr template);
[DllImport("kernel32.dll", SetLastError = true)]
static extern bool ReadFile(IntPtr handle, byte[] buffer, int bytesToRead, out int bytesRead, IntPtr overlapped);
[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
static extern bool WriteConsole(IntPtr handle, string buffer, int charactersToWrite, out int charactersWritten, IntPtr reserved);
