using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RobloxKeeper
{
    // Which processes have a file open, as Windows itself knows it.
    //
    // Windows looks through every open handle on the PC to answer - measured,
    // about 20 ms - so this is asked once per file and the answer kept, not
    // asked every second.
    static class OpenFiles
    {
        // Every process holding the file open - empty when none is, or the
        // file is gone. Null when Windows couldn't say, which is not the same
        // as nobody.
        public static IList<int> Holders(string path)
        {
            try
            {
                using (SafeFileHandle h = Native.CreateFile(path, Native.FILE_READ_ATTRIBUTES,
                           Native.FILE_SHARE_READ_WRITE_DELETE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero))
                {
                    if (h.IsInvalid)
                    {
                        int error = Marshal.GetLastWin32Error();
                        return error == Native.ERROR_FILE_NOT_FOUND || error == Native.ERROR_PATH_NOT_FOUND
                            ? new List<int>() : null;
                    }

                    // A count, then that many ids. Too small a buffer is
                    // answered with a mismatch, and a bigger one is tried.
                    for (int size = 1024; size <= 1024 * 1024; size *= 8)
                    {
                        IntPtr buf = Marshal.AllocHGlobal(size);
                        try
                        {
                            Native.IO_STATUS_BLOCK io;
                            int status = Native.NtQueryInformationFile(h, out io, buf, size,
                                Native.FileProcessIdsUsingFileInformation);
                            if (status == Native.STATUS_INFO_LENGTH_MISMATCH) continue;
                            if (status != 0) return null;

                            int count = Marshal.ReadInt32(buf);
                            List<int> pids = new List<int>(count);
                            for (int i = 0; i < count; i++)
                                pids.Add((int)Marshal.ReadIntPtr(buf, IntPtr.Size * (i + 1)).ToInt64());
                            return pids;
                        }
                        finally { Marshal.FreeHGlobal(buf); }
                    }
                    return null;
                }
            }
            catch { return null; }   // an unusual Windows - not a reason to fail
        }
    }
}
